namespace Conversa.Domain.Conversations;

public sealed class ConversationContextSummary
{
    public const int SummaryMaxLength = 8_000;
    public const int PromptVersionMaxLength = 64;

    private ConversationContextSummary()
    {
        Summary = string.Empty;
        PromptVersion = "unknown";
    }

    public Guid ConversationId { get; private set; }

    public string Summary { get; private set; }

    public string PromptVersion { get; private set; }

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
        DateTimeOffset updatedAt,
        string promptVersion = "unknown")
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

        ArgumentException.ThrowIfNullOrWhiteSpace(promptVersion);
        var normalizedVersion = promptVersion.Trim();
        if (normalizedVersion.Length > PromptVersionMaxLength)
        {
            throw new ArgumentException("Prompt version is too long.", nameof(promptVersion));
        }

        return new ConversationContextSummary
        {
            ConversationId = conversationId,
            Summary = normalizedSummary,
            PromptVersion = normalizedVersion,
            ThroughCreatedAt = throughCreatedAt,
            ThroughMessageId = throughMessageId,
            UpdatedAt = updatedAt,
            Revision = 1
        };
    }
}
