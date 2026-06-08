namespace OrderService.Infrastructure.Persistence;

public sealed class InboxMessage
{
    public Guid MessageId { get; private set; }
    public string MessageType { get; private set; } = default!;
    public DateTimeOffset ProcessedAt { get; private set; }

    private InboxMessage()
    {
    }

    public InboxMessage(Guid messageId, string messageType)
    {
        if (messageId == Guid.Empty)
        {
            throw new ArgumentException("MessageId is required", nameof(messageId));
        }

        if (string.IsNullOrWhiteSpace(messageType))
        {
            throw new ArgumentException("MessageType is required", nameof(messageType));
        }

        MessageId = messageId;
        MessageType = messageType;
        ProcessedAt = DateTimeOffset.UtcNow;
    }
}
