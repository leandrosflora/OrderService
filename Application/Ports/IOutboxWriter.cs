namespace OrderService.Application.Ports;

public interface IOutboxWriter
{
    Task AddAsync<T>(
        string topic,
        string aggregateKey,
        T message,
        CancellationToken cancellationToken);
}
