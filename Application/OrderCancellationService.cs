using Microsoft.EntityFrameworkCore;
using OrderService.Application.Ports;
using OrderService.Contracts;
using OrderService.Domain;
using OrderService.Infrastructure.Persistence;

namespace OrderService.Application;

public sealed class OrderCancellationService
{
    private readonly OrderDbContext _dbContext;
    private readonly IOutboxWriter _outbox;

    public OrderCancellationService(OrderDbContext dbContext, IOutboxWriter outbox)
    {
        _dbContext = dbContext;
        _outbox = outbox;
    }

    public async Task CancelAsync(
        Guid orderId,
        string reason,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var commandId = CreateDeterministicId(orderId, idempotencyKey);
        var processed = await _dbContext.InboxMessages.AnyAsync(
            x => x.MessageId == commandId,
            cancellationToken);

        if (processed)
        {
            return;
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var order = await _dbContext.Orders
            .SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken)
            ?? throw new KeyNotFoundException("Order not found");

        if (order.Status != OrderStatus.Cancelled)
        {
            order.Cancel(reason);
            await WriteCompensationsAsync(order, cancellationToken);

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

        await _dbContext.InboxMessages.AddAsync(new InboxMessage(commandId, "CancelOrderCommand"), cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task WriteCompensationsAsync(Domain.Order order, CancellationToken cancellationToken)
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
    }

    private static Guid CreateDeterministicId(Guid orderId, string idempotencyKey)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{orderId}:{idempotencyKey}"));

        return new Guid(bytes[..16]);
    }
}
