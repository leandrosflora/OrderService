using Microsoft.EntityFrameworkCore;
using OrderService.Application.Ports;
using OrderService.Contracts;
using OrderService.Domain;
using OrderService.Infrastructure.Persistence;

namespace OrderService.Application;

public sealed class OrderProcessManager
{
    private readonly OrderDbContext _dbContext;
    private readonly IOutboxWriter _outbox;

    public OrderProcessManager(OrderDbContext dbContext, IOutboxWriter outbox)
    {
        _dbContext = dbContext;
        _outbox = outbox;
    }

    public async Task HandleAsync(CheckoutConfirmedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        await ExecuteOnceAsync(
            integrationEvent.MessageId,
            nameof(CheckoutConfirmedIntegrationEvent),
            async () =>
            {
                var existing = await _dbContext.Orders.AnyAsync(
                    x => x.CheckoutId == integrationEvent.CheckoutId,
                    cancellationToken);

                if (existing)
                {
                    return;
                }

                var items = integrationEvent.Items
                    .Select(x => new OrderItem(x.SkuId, x.Title, x.Quantity, x.UnitPrice))
                    .ToList();

                var order = Order.Create(
                    integrationEvent.CheckoutId,
                    integrationEvent.BuyerId,
                    integrationEvent.SellerId,
                    integrationEvent.Currency,
                    integrationEvent.ShippingPrice,
                    integrationEvent.ShippingPromiseId,
                    integrationEvent.PricingQuoteId,
                    items);

                await _dbContext.Orders.AddAsync(order, cancellationToken);

                await _outbox.AddAsync(
                    topic: "inventory.commands",
                    aggregateKey: order.Id.ToString(),
                    message: new ReserveInventoryCommand(
                        Guid.NewGuid(),
                        order.Id,
                        order.SellerId,
                        integrationEvent.Items.Select(x =>
                            new InventoryReservationItem(x.SkuId, x.FulfillmentCenterId, x.Quantity)).ToList()),
                    cancellationToken);

                var fulfillmentCenterId = integrationEvent.Items
                    .Select(x => x.FulfillmentCenterId)
                    .Distinct()
                    .Single();

                await _outbox.AddAsync(
                    topic: "fulfillment.commands",
                    aggregateKey: order.Id.ToString(),
                    message: new ReserveFulfillmentCapacityCommand(
                        Guid.NewGuid(),
                        order.Id,
                        fulfillmentCenterId,
                        CalculateCapacityUnits(integrationEvent.Items)),
                    cancellationToken);
            },
            cancellationToken);
    }

    public async Task HandleAsync(InventoryReservedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        await ExecuteOnceAsync(
            integrationEvent.MessageId,
            nameof(InventoryReservedIntegrationEvent),
            async () =>
            {
                var order = await GetOrderAsync(integrationEvent.OrderId, cancellationToken);
                var shouldAuthorizePayment = order.MarkInventoryReserved(integrationEvent.ReservationId);

                if (shouldAuthorizePayment)
                {
                    await WritePaymentAuthorizationAsync(order, cancellationToken);
                }
            },
            cancellationToken);
    }

    public async Task HandleAsync(FulfillmentCapacityReservedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        await ExecuteOnceAsync(
            integrationEvent.MessageId,
            nameof(FulfillmentCapacityReservedIntegrationEvent),
            async () =>
            {
                var order = await GetOrderAsync(integrationEvent.OrderId, cancellationToken);
                var shouldAuthorizePayment = order.MarkCapacityReserved(integrationEvent.ReservationId);

                if (shouldAuthorizePayment)
                {
                    await WritePaymentAuthorizationAsync(order, cancellationToken);
                }
            },
            cancellationToken);
    }

    public async Task HandleAsync(PaymentAuthorizedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        await ExecuteOnceAsync(
            integrationEvent.MessageId,
            nameof(PaymentAuthorizedIntegrationEvent),
            async () =>
            {
                var order = await GetOrderAsync(integrationEvent.OrderId, cancellationToken);
                order.MarkPaymentAuthorized(integrationEvent.PaymentAuthorizationId);

                await _outbox.AddAsync(
                    "inventory.commands",
                    order.Id.ToString(),
                    new ConfirmInventoryReservationCommand(Guid.NewGuid(), order.Id, order.InventoryReservationId!.Value),
                    cancellationToken);

                await _outbox.AddAsync(
                    "fulfillment.commands",
                    order.Id.ToString(),
                    new ConfirmFulfillmentCapacityCommand(Guid.NewGuid(), order.Id, order.CapacityReservationId!.Value),
                    cancellationToken);
            },
            cancellationToken);
    }

    public async Task HandleAsync(InventoryReservationConfirmedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        await ExecuteOnceAsync(
            integrationEvent.MessageId,
            nameof(InventoryReservationConfirmedIntegrationEvent),
            async () =>
            {
                var order = await GetOrderAsync(integrationEvent.OrderId, cancellationToken);
                var shouldCreateShipment = order.MarkInventoryConfirmed();

                if (shouldCreateShipment)
                {
                    await WriteShipmentCreationAsync(order, cancellationToken);
                }
            },
            cancellationToken);
    }

    public async Task HandleAsync(FulfillmentCapacityConfirmedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        await ExecuteOnceAsync(
            integrationEvent.MessageId,
            nameof(FulfillmentCapacityConfirmedIntegrationEvent),
            async () =>
            {
                var order = await GetOrderAsync(integrationEvent.OrderId, cancellationToken);
                var shouldCreateShipment = order.MarkCapacityConfirmed();

                if (shouldCreateShipment)
                {
                    await WriteShipmentCreationAsync(order, cancellationToken);
                }
            },
            cancellationToken);
    }

    public async Task HandleAsync(ShipmentCreatedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        await ExecuteOnceAsync(
            integrationEvent.MessageId,
            nameof(ShipmentCreatedIntegrationEvent),
            async () =>
            {
                var order = await GetOrderAsync(integrationEvent.OrderId, cancellationToken);
                order.MarkShipmentCreated(integrationEvent.ShipmentId);

                await _outbox.AddAsync(
                    "payment.commands",
                    order.Id.ToString(),
                    new CapturePaymentCommand(
                        Guid.NewGuid(),
                        order.Id,
                        order.PaymentAuthorizationId!.Value,
                        order.TotalAmount,
                        order.Currency),
                    cancellationToken);
            },
            cancellationToken);
    }

    public async Task HandleAsync(PaymentCapturedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        await ExecuteOnceAsync(
            integrationEvent.MessageId,
            nameof(PaymentCapturedIntegrationEvent),
            async () =>
            {
                var order = await GetOrderAsync(integrationEvent.OrderId, cancellationToken);
                order.MarkPaymentCaptured();

                await _outbox.AddAsync(
                    "order.events",
                    order.Id.ToString(),
                    new OrderConfirmedIntegrationEvent(
                        Guid.NewGuid(),
                        order.Id,
                        order.CheckoutId,
                        order.BuyerId,
                        order.SellerId,
                        order.TotalAmount,
                        order.Currency,
                        order.ShipmentId,
                        order.ConfirmedAt),
                    cancellationToken);
            },
            cancellationToken);
    }

    public async Task HandleAsync(InventoryReservationFailedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        await ExecuteOnceAsync(
            integrationEvent.MessageId,
            nameof(InventoryReservationFailedIntegrationEvent),
            async () =>
            {
                var order = await GetOrderAsync(integrationEvent.OrderId, cancellationToken);
                order.MarkInventoryReservationFailed(integrationEvent.Reason);
                await WriteCompensationsAndCancellationEventAsync(order, cancellationToken);
            },
            cancellationToken);
    }

    public async Task HandleAsync(FulfillmentCapacityReservationFailedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        await ExecuteOnceAsync(
            integrationEvent.MessageId,
            nameof(FulfillmentCapacityReservationFailedIntegrationEvent),
            async () =>
            {
                var order = await GetOrderAsync(integrationEvent.OrderId, cancellationToken);
                order.MarkCapacityReservationFailed(integrationEvent.Reason);
                await WriteCompensationsAndCancellationEventAsync(order, cancellationToken);
            },
            cancellationToken);
    }

    public async Task HandleAsync(PaymentAuthorizationFailedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        await ExecuteOnceAsync(
            integrationEvent.MessageId,
            nameof(PaymentAuthorizationFailedIntegrationEvent),
            async () =>
            {
                var order = await GetOrderAsync(integrationEvent.OrderId, cancellationToken);
                order.MarkPaymentAuthorizationFailed(integrationEvent.Reason);
                await WriteCompensationsAndCancellationEventAsync(order, cancellationToken);
            },
            cancellationToken);
    }

    public async Task HandleAsync(ShipmentCreationFailedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        await ExecuteOnceAsync(
            integrationEvent.MessageId,
            nameof(ShipmentCreationFailedIntegrationEvent),
            async () =>
            {
                var order = await GetOrderAsync(integrationEvent.OrderId, cancellationToken);
                order.MarkShipmentCreationFailed(integrationEvent.Reason);
                await WriteCompensationsAndCancellationEventAsync(order, cancellationToken);
            },
            cancellationToken);
    }

    public async Task HandleAsync(PaymentCaptureFailedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        await ExecuteOnceAsync(
            integrationEvent.MessageId,
            nameof(PaymentCaptureFailedIntegrationEvent),
            async () =>
            {
                var order = await GetOrderAsync(integrationEvent.OrderId, cancellationToken);
                order.MarkPaymentCaptureFailed();
            },
            cancellationToken);
    }

    private Task WritePaymentAuthorizationAsync(Order order, CancellationToken cancellationToken)
    {
        return _outbox.AddAsync(
            "payment.commands",
            order.Id.ToString(),
            new AuthorizePaymentCommand(
                Guid.NewGuid(),
                order.Id,
                order.BuyerId,
                order.TotalAmount,
                order.Currency,
                PaymentMethodToken: $"checkout:{order.CheckoutId}"),
            cancellationToken);
    }

    private Task WriteShipmentCreationAsync(Order order, CancellationToken cancellationToken)
    {
        return _outbox.AddAsync(
            "shipment.commands",
            order.Id.ToString(),
            new CreateShipmentCommand(
                Guid.NewGuid(),
                order.Id,
                order.ShippingPromiseId,
                order.InventoryReservationId!.Value,
                order.CapacityReservationId!.Value),
            cancellationToken);
    }

    private async Task WriteCompensationsAndCancellationEventAsync(Order order, CancellationToken cancellationToken)
    {
        if (order.InventoryReservationId.HasValue)
        {
            await _outbox.AddAsync(
                "inventory.commands",
                order.Id.ToString(),
                new ReleaseInventoryReservationCommand(Guid.NewGuid(), order.Id, order.InventoryReservationId.Value),
                cancellationToken);
        }

        if (order.CapacityReservationId.HasValue)
        {
            await _outbox.AddAsync(
                "fulfillment.commands",
                order.Id.ToString(),
                new ReleaseFulfillmentCapacityCommand(Guid.NewGuid(), order.Id, order.CapacityReservationId.Value),
                cancellationToken);
        }

        if (order.PaymentAuthorizationId.HasValue)
        {
            await _outbox.AddAsync(
                "payment.commands",
                order.Id.ToString(),
                new VoidPaymentAuthorizationCommand(Guid.NewGuid(), order.Id, order.PaymentAuthorizationId.Value),
                cancellationToken);
        }

        await _outbox.AddAsync(
            "order.events",
            order.Id.ToString(),
            new OrderCancelledIntegrationEvent(
                Guid.NewGuid(),
                order.Id,
                order.Status.ToString(),
                order.CancellationReason),
            cancellationToken);
    }

    private async Task<Order> GetOrderAsync(Guid orderId, CancellationToken cancellationToken)
    {
        return await _dbContext.Orders
            .Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken)
            ?? throw new KeyNotFoundException("Order not found");
    }

    private static int CalculateCapacityUnits(IReadOnlyList<CheckoutConfirmedItem> items)
    {
        return Math.Max(1, items.Sum(x => x.Quantity));
    }

    private async Task ExecuteOnceAsync(
        Guid messageId,
        string messageType,
        Func<Task> action,
        CancellationToken cancellationToken)
    {
        var alreadyProcessed = await _dbContext.InboxMessages.AnyAsync(
            x => x.MessageId == messageId,
            cancellationToken);

        if (alreadyProcessed)
        {
            return;
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        await action();

        await _dbContext.InboxMessages.AddAsync(new InboxMessage(messageId, messageType), cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
