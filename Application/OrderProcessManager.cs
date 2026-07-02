using Microsoft.EntityFrameworkCore;
using OrderService.Application.Ports;
using OrderService.Contracts;
using OrderService.Domain;
using Microsoft.Extensions.Options;
using OrderService.Infrastructure.Messaging;
using OrderService.Infrastructure.Persistence;

namespace OrderService.Application;

public sealed class OrderProcessManager
{
    private readonly OrderDbContext _dbContext;
    private readonly IOutboxWriter _outbox;
    private readonly KafkaOptions _kafkaOptions;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<OrderProcessManager> _logger;

    public OrderProcessManager(
        OrderDbContext dbContext,
        IOutboxWriter outbox,
        IOptions<KafkaOptions> kafkaOptions,
        IHostEnvironment environment,
        ILogger<OrderProcessManager> logger)
    {
        _dbContext = dbContext;
        _outbox = outbox;
        _kafkaOptions = kafkaOptions.Value;
        _environment = environment;
        _logger = logger;
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

                var orderCreatedEventId = Guid.NewGuid();

                await _outbox.AddAsync(
                    topic: _kafkaOptions.Topics.OrderCreated,
                    aggregateKey: order.Id.ToString(),
                    message: new IntegrationEventEnvelope<OrderCreatedIntegrationEvent>(
                        orderCreatedEventId,
                        _kafkaOptions.Topics.OrderCreated,
                        "1.0",
                        DateTimeOffset.UtcNow,
                        integrationEvent.MessageId.ToString(),
                        "order-service",
                        CreateOrderCreatedIntegrationEvent(orderCreatedEventId, order, integrationEvent)),
                    cancellationToken);

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
                var wasAlreadyReserved = order.InventoryState is ReservationState.Reserved or ReservationState.Confirmed;
                var shouldAuthorizePayment = order.MarkInventoryReserved(integrationEvent.ReservationId);

                if (wasAlreadyReserved)
                {
                    _logger.LogInformation(
                        "InventoryReservedIntegrationEvent ignored for order {OrderId}: inventory already {InventoryState}. Capacity state: {CapacityState}",
                        order.Id, order.InventoryState, order.CapacityState);
                }
                else
                {
                    _logger.LogInformation(
                        "Inventory reserved for order {OrderId}. Inventory state: {InventoryState}, capacity state: {CapacityState}",
                        order.Id, order.InventoryState, order.CapacityState);
                }

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
                var wasAlreadyReserved = order.CapacityState is ReservationState.Reserved or ReservationState.Confirmed;
                var shouldAuthorizePayment = order.MarkCapacityReserved(integrationEvent.ReservationId);

                if (wasAlreadyReserved)
                {
                    _logger.LogInformation(
                        "FulfillmentCapacityReservedIntegrationEvent ignored for order {OrderId}: capacity already {CapacityState}. Inventory state: {InventoryState}",
                        order.Id, order.CapacityState, order.InventoryState);
                }
                else
                {
                    _logger.LogInformation(
                        "Fulfillment capacity reserved for order {OrderId}. Capacity state: {CapacityState}, inventory state: {InventoryState}",
                        order.Id, order.CapacityState, order.InventoryState);
                }

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


    public async Task HandleShipmentStatusUpdatedAsync(
        Guid eventId,
        ShipmentStatusUpdatedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        await ExecuteOnceAsync(
            eventId,
            nameof(ShipmentStatusUpdatedIntegrationEvent),
            async () =>
            {
                var order = await GetOrderAsync(integrationEvent.OrderId, cancellationToken);
                order.UpdateShipmentStatus(
                    integrationEvent.ShipmentId,
                    integrationEvent.CurrentStatus,
                    integrationEvent.StatusDate);
            },
            cancellationToken);
    }

    private OrderCreatedIntegrationEvent CreateOrderCreatedIntegrationEvent(
        Guid messageId,
        Order order,
        CheckoutConfirmedIntegrationEvent checkoutEvent)
    {
        var routeId = GetRequiredShipmentValue(checkoutEvent.RouteId, nameof(checkoutEvent.RouteId), order);
        var carrierCode = GetRequiredShipmentValue(checkoutEvent.CarrierCode, nameof(checkoutEvent.CarrierCode), order);
        var serviceLevelCode = GetRequiredShipmentValue(checkoutEvent.ServiceLevelCode, nameof(checkoutEvent.ServiceLevelCode), order);
        var originNodeId = checkoutEvent.OriginNodeId ?? GetMockOriginNodeId(order, checkoutEvent);
        var promisedDeliveryDate = checkoutEvent.PromisedDeliveryDate ?? GetMockPromisedDeliveryDate(order);
        var destination = checkoutEvent.Destination ?? GetMockDestination(order);
        var packages = checkoutEvent.Packages is { Count: > 0 }
            ? checkoutEvent.Packages
            : GetMockPackages(order, checkoutEvent);

        return new OrderCreatedIntegrationEvent(
            messageId,
            order.Id,
            order.CheckoutId,
            order.BuyerId,
            order.SellerId,
            order.ShippingPromiseId,
            routeId,
            carrierCode,
            serviceLevelCode,
            originNodeId,
            promisedDeliveryDate,
            destination,
            packages,
            order.TotalAmount,
            order.Currency,
            order.CreatedAt);
    }

    private string GetRequiredShipmentValue(string? value, string fieldName, Order order)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        if (!_environment.IsDevelopment())
        {
            throw new InvalidOperationException($"CheckoutConfirmedIntegrationEvent is missing required shipment field {fieldName} for order {order.Id}");
        }

        var fallback = fieldName switch
        {
            nameof(CheckoutConfirmedIntegrationEvent.RouteId) => $"route_{order.ShippingPromiseId}",
            nameof(CheckoutConfirmedIntegrationEvent.CarrierCode) => "carrier_mock",
            nameof(CheckoutConfirmedIntegrationEvent.ServiceLevelCode) => "standard",
            _ => throw new InvalidOperationException($"No Development fallback is configured for {fieldName}")
        };

        _logger.LogWarning(
            "CheckoutConfirmedIntegrationEvent missing required shipment field {FieldName} for order {OrderId}; using Development fallback {Fallback}",
            fieldName,
            order.Id,
            fallback);

        return fallback;
    }

    private Guid GetMockOriginNodeId(Order order, CheckoutConfirmedIntegrationEvent checkoutEvent)
    {
        if (!_environment.IsDevelopment())
        {
            throw new InvalidOperationException($"CheckoutConfirmedIntegrationEvent is missing required shipment field {nameof(checkoutEvent.OriginNodeId)} for order {order.Id}");
        }

        var fallback = checkoutEvent.Items.Select(x => x.FulfillmentCenterId).FirstOrDefault(x => x != Guid.Empty);
        if (fallback == Guid.Empty)
        {
            fallback = order.SellerId;
        }

        _logger.LogWarning(
            "CheckoutConfirmedIntegrationEvent missing required shipment field {FieldName} for order {OrderId}; using Development fallback {Fallback}",
            nameof(checkoutEvent.OriginNodeId),
            order.Id,
            fallback);

        return fallback;
    }

    private DateOnly GetMockPromisedDeliveryDate(Order order)
    {
        if (!_environment.IsDevelopment())
        {
            throw new InvalidOperationException($"CheckoutConfirmedIntegrationEvent is missing required shipment field PromisedDeliveryDate for order {order.Id}");
        }

        var fallback = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(1));
        _logger.LogWarning(
            "CheckoutConfirmedIntegrationEvent missing required shipment field {FieldName} for order {OrderId}; using Development fallback {Fallback}",
            nameof(CheckoutConfirmedIntegrationEvent.PromisedDeliveryDate),
            order.Id,
            fallback);
        return fallback;
    }

    private OrderDestinationDto GetMockDestination(Order order)
    {
        if (!_environment.IsDevelopment())
        {
            throw new InvalidOperationException($"CheckoutConfirmedIntegrationEvent is missing required shipment field Destination for order {order.Id}");
        }

        _logger.LogWarning(
            "CheckoutConfirmedIntegrationEvent missing required shipment field {FieldName} for order {OrderId}; using controlled Development fallback",
            nameof(CheckoutConfirmedIntegrationEvent.Destination),
            order.Id);

        return new OrderDestinationDto("Av. Paulista", "1000", "São Paulo", "SP", "01310-100", "BR");
    }

    private IReadOnlyList<OrderPackageDto> GetMockPackages(Order order, CheckoutConfirmedIntegrationEvent checkoutEvent)
    {
        if (!_environment.IsDevelopment())
        {
            throw new InvalidOperationException($"CheckoutConfirmedIntegrationEvent is missing required shipment field Packages for order {order.Id}");
        }

        _logger.LogWarning(
            "CheckoutConfirmedIntegrationEvent missing required shipment field {FieldName} for order {OrderId}; deriving Development fallback from checkout items",
            nameof(CheckoutConfirmedIntegrationEvent.Packages),
            order.Id);

        return
        [
            new OrderPackageDto(
                $"pkg_{order.Id:N}",
                1.0m,
                10m,
                20m,
                30m,
                checkoutEvent.Items
                    .Select(x => new OrderPackageItemDto(x.SkuId, x.Quantity))
                    .ToList())
        ];
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
