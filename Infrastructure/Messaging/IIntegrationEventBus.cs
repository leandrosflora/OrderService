namespace OrderService.Infrastructure.Messaging;

public interface IIntegrationEventBus
{
    Task PublishAsync(
        string topic,
        string key,
        string payload,
        string messageType,
        CancellationToken cancellationToken);
}
