using Confluent.Kafka;

namespace OrderService.Infrastructure.Messaging;

public sealed class KafkaIntegrationEventBus : IIntegrationEventBus
{
    private readonly IProducer<string, string> _producer;
    private readonly ILogger<KafkaIntegrationEventBus> _logger;

    public KafkaIntegrationEventBus(IProducer<string, string> producer, ILogger<KafkaIntegrationEventBus> logger)
    {
        _producer = producer;
        _logger = logger;
    }

    public async Task PublishAsync(
        string topic,
        string key,
        string payload,
        string messageType,
        CancellationToken cancellationToken)
    {
        var eventType = TryReadEnvelopeValue(payload, "eventType") ?? ResolveEventType(messageType);
        var correlationId = TryReadEnvelopeValue(payload, "correlationId") ?? key;

        var result = await _producer.ProduceAsync(
            topic,
            new Message<string, string>
            {
                Key = key,
                Value = payload,
                Headers = new Headers
                {
                    { "eventType", System.Text.Encoding.UTF8.GetBytes(eventType) },
                    { "correlationId", System.Text.Encoding.UTF8.GetBytes(correlationId) }
                }
            },
            cancellationToken);

        _logger.LogInformation(
            "Published Kafka message to topic {Topic} with key {Key}, eventType {EventType}, correlationId {CorrelationId}, partition {Partition}, offset {Offset}",
            topic,
            key,
            eventType,
            correlationId,
            result.Partition.Value,
            result.Offset.Value);
    }

    private static string ResolveEventType(string messageType)
    {
        return messageType switch
        {
            nameof(OrderService.Contracts.OrderCreatedIntegrationEvent) => "order.created",
            nameof(OrderService.Contracts.ShipmentStatusUpdatedIntegrationEvent) => "shipment.status.updated",
            _ => messageType
        };
    }

    private static string? TryReadEnvelopeValue(string payload, string propertyName)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(payload);
            return document.RootElement.TryGetProperty(propertyName, out var value) ? value.GetString() : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
