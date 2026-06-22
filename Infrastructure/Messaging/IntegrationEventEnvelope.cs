using System.Text.Json.Serialization;

namespace OrderService.Infrastructure.Messaging;

public sealed record IntegrationEventEnvelope<T>(
    [property: JsonPropertyName("eventId")] Guid EventId,
    [property: JsonPropertyName("eventType")] string EventType,
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("occurredAt")] DateTimeOffset OccurredAt,
    [property: JsonPropertyName("correlationId")] string CorrelationId,
    [property: JsonPropertyName("producer")] string Producer,
    [property: JsonPropertyName("payload")] T Payload);

internal sealed record SagaPayload(
    [property: JsonPropertyName("orderId")] Guid OrderId,
    [property: JsonPropertyName("reservationId")] Guid ReservationId);

internal sealed record SagaFailurePayload(
    [property: JsonPropertyName("orderId")] Guid OrderId,
    [property: JsonPropertyName("reason")] string? Reason);
