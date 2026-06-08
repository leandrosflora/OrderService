namespace OrderService.Infrastructure.Messaging;

public sealed class KafkaIntegrationEventBus : IIntegrationEventBus
{
    private readonly ILogger<KafkaIntegrationEventBus> _logger;

    public KafkaIntegrationEventBus(ILogger<KafkaIntegrationEventBus> logger)
    {
        _logger = logger;
    }

    public Task PublishAsync(
        string topic,
        string key,
        string payload,
        string messageType,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Publishing {MessageType} to topic {Topic} with key {Key}: {Payload}",
            messageType,
            topic,
            key,
            payload);

        return Task.CompletedTask;
    }
}
