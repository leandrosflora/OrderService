namespace OrderService.Infrastructure.Messaging;

public sealed class KafkaOptions
{
    public const string SectionName = "Kafka";

    public string BootstrapServers { get; init; } = "localhost:9092";
    public string ConsumerGroupId { get; init; } = "order-service";
    public KafkaTopics Topics { get; init; } = new();
}

public sealed class KafkaTopics
{
    public string OrderCreated { get; init; } = "order.created";
    public string ShipmentStatusUpdated { get; init; } = "shipment.status.updated";
}
