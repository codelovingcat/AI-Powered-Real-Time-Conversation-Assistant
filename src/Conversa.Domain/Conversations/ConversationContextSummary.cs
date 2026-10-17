namespace Conversa.Domain.Conversations;

public sealed class ConversationContextSummary
{
    public const int SummaryMaxLength = 8_000;

    private ConversationContextSummary()
    {
        Summary = string.Empty;
    }

    public Guid ConversationId { get; private set; }

    public string Summary { get; private set; }

    /// <summary>The last transcript message represented by this summary.</summary>
    public DateTimeOffset ThroughCreatedAt { get; private set; }

    public Guid ThroughMessageId { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Monotonically incremented to prevent an older concurrent refresh replacing newer context.</summary>
    public int Revision { get; private set; }

    public static ConversationContextSummary Create(
        Guid conversationId,
        string summary,
        DateTimeOffset throughCreatedAt,
        Guid throughMessageId,
        DateTimeOffset updatedAt)
    {
        if (conversationId == Guid.Empty)
        {
            throw new ArgumentException("Conversation id is required.", nameof(conversationId));
        }

        if (throughMessageId == Guid.Empty)
        {
            throw new ArgumentException("Summary cursor message id is required.", nameof(throughMessageId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        var normalizedSummary = summary.Trim();
        if (normalizedSummary.Length > SummaryMaxLength)
        {
            throw new ArgumentException(
                $"Conversation summary cannot exceed {SummaryMaxLength} characters.",
                nameof(summary));
        }

        return new ConversationContextSummary
        {
            ConversationId = conversationId,
            Summary = normalizedSummary,
            ThroughCreatedAt = throughCreatedAt,
            ThroughMessageId = throughMessageId,
            UpdatedAt = updatedAt,
            Revision = 1
        };
    }
}
