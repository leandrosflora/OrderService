namespace OrderService.Infrastructure.Outbox;

public sealed class OutboxMessage
{
    public Guid Id { get; private set; }
    public string Topic { get; private set; } = default!;
    public string MessageType { get; private set; } = default!;
    public string AggregateKey { get; private set; } = default!;
    public string Payload { get; private set; } = default!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset? NextAttemptAt { get; private set; }
    public string? LastError { get; private set; }

    private OutboxMessage()
    {
    }

    public OutboxMessage(string topic, string messageType, string aggregateKey, string payload)
    {
        if (string.IsNullOrWhiteSpace(topic))
        {
            throw new ArgumentException("Topic is required", nameof(topic));
        }

        if (string.IsNullOrWhiteSpace(messageType))
        {
            throw new ArgumentException("MessageType is required", nameof(messageType));
        }

        if (string.IsNullOrWhiteSpace(aggregateKey))
        {
            throw new ArgumentException("AggregateKey is required", nameof(aggregateKey));
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            throw new ArgumentException("Payload is required", nameof(payload));
        }

        Id = Guid.NewGuid();
        Topic = topic;
        MessageType = messageType;
        AggregateKey = aggregateKey;
        Payload = payload;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkProcessed()
    {
        ProcessedAt = DateTimeOffset.UtcNow;
        LastError = null;
        NextAttemptAt = null;
    }

    public void MarkFailed(string error)
    {
        Attempts++;
        LastError = error;

        var delaySeconds = Math.Min(300, Math.Pow(2, Attempts));
        NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(delaySeconds);
    }
}
