using Microsoft.EntityFrameworkCore;
using OrderService.Application.Ports;
using OrderService.Domain;

namespace OrderService.Infrastructure.Persistence;

public sealed class OrderRepository : IOrderRepository
{
    private readonly OrderDbContext _dbContext;

    public OrderRepository(OrderDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Order?> GetByIdAsync(Guid orderId, CancellationToken cancellationToken)
    {
        return _dbContext.Orders
            .Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
    }

    public Task<Order?> GetByCheckoutIdAsync(Guid checkoutId, CancellationToken cancellationToken)
    {
        return _dbContext.Orders
            .Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.CheckoutId == checkoutId, cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }
}
