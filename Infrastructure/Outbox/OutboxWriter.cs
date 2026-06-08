using System.Text.Json;
using OrderService.Application.Ports;
using OrderService.Infrastructure.Persistence;

namespace OrderService.Infrastructure.Outbox;

public sealed class OutboxWriter : IOutboxWriter
{
    private readonly OrderDbContext _dbContext;

    public OutboxWriter(OrderDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync<T>(
        string topic,
        string aggregateKey,
        T message,
        CancellationToken cancellationToken)
    {
        var outboxMessage = new OutboxMessage(
            topic,
            typeof(T).Name,
            aggregateKey,
            JsonSerializer.Serialize(message));

        await _dbContext.OutboxMessages.AddAsync(outboxMessage, cancellationToken);
    }
}
