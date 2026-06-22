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
    public string CheckoutConfirmed { get; init; } = "checkout.confirmed";
    public string InventoryReserved { get; init; } = "inventory.reserved";
    public string InventoryReservationConfirmed { get; init; } = "inventory.reservation.confirmed";
    public string InventoryReservationFailed { get; init; } = "inventory.reservation.failed";
    public string FulfillmentCapacityReserved { get; init; } = "fulfillment.capacity.reserved";
    public string FulfillmentCapacityConfirmed { get; init; } = "fulfillment.capacity.confirmed";
    public string FulfillmentCapacityFailed { get; init; } = "fulfillment.capacity.failed";
    public string ShipmentCreated { get; init; } = "shipment.created";
    public string ShipmentCreationFailed { get; init; } = "shipment.creation.failed";
}
