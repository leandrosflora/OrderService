using System.Text.Json;
using OrderService.Contracts;
using OrderService.Infrastructure.Messaging;

namespace OrderService.Tests.Application;

public sealed class KafkaEnvelopeTests
{
    [Fact]
    public void IntegrationEventEnvelope_SerializesUsingCanonicalKafkaEnvelopeFields()
    {
        var payload = new OrderCancelledIntegrationEvent(Guid.NewGuid(), Guid.NewGuid(), "Cancelled", "buyer requested");
        var envelope = new IntegrationEventEnvelope<OrderCancelledIntegrationEvent>(
            Guid.NewGuid(),
            "order.cancelled",
            "1.0",
            DateTimeOffset.Parse("2026-06-14T12:00:00Z"),
            "correlation-123",
            "order-service",
            payload);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(envelope));
        var root = document.RootElement;

        Assert.True(root.TryGetProperty("eventId", out _));
        Assert.Equal("order.cancelled", root.GetProperty("eventType").GetString());
        Assert.Equal("1.0", root.GetProperty("schemaVersion").GetString());
        Assert.True(root.TryGetProperty("occurredAt", out _));
        Assert.Equal("correlation-123", root.GetProperty("correlationId").GetString());
        Assert.Equal("order-service", root.GetProperty("producer").GetString());
        Assert.True(root.TryGetProperty("payload", out _));
    }
}
