using Conversa.Domain.Conversations;

namespace Conversa.Application.Abstractions.Persistence;

public interface IConversationContextSummaryRepository
{
    Task<ConversationContextSummary?> GetByConversationIdAsync(
        Guid conversationId,
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets the oldest not-yet-summarized messages strictly after the summary cursor and before the verbatim recent window.
    /// Results are ordered oldest-first so the persisted cursor advances monotonically.
    /// </summary>
    Task<IReadOnlyList<Message>> ListUnsummarizedMessagesAsync(
        Guid conversationId,
        Guid userId,
        DateTimeOffset? afterCreatedAt,
        Guid? afterMessageId,
        DateTimeOffset beforeCreatedAt,
        Guid beforeMessageId,
        int take,
        CancellationToken cancellationToken);

    /// <summary>
    /// Commits a summary only if the previously read revision is still current.
    /// Returns false when a concurrent refresh has already advanced the record.
    /// </summary>
    Task<bool> TrySaveAsync(
        Guid conversationId,
        Guid userId,
        int expectedRevision,
        string summary,
        DateTimeOffset throughCreatedAt,
        Guid throughMessageId,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken);
}
