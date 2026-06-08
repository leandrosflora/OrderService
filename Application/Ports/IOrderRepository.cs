namespace OrderService.Application.Ports;

public interface IOrderRepository
{
    Task<Domain.Order?> GetByIdAsync(
        Guid orderId,
        CancellationToken cancellationToken);

    Task<Domain.Order?> GetByCheckoutIdAsync(
        Guid checkoutId,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
