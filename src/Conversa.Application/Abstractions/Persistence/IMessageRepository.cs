using Conversa.Domain.Conversations;

namespace Conversa.Application.Abstractions.Persistence;

public interface IMessageRepository
{
    Task<IReadOnlyList<Message>> ListByConversationAsync(
        Guid conversationId,
        int take,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Message>> ListByConversationPageAsync(
        Guid conversationId,
        int take,
        DateTimeOffset? beforeCreatedAt,
        Guid? beforeId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Message>> ListRecentAsync(
        Guid conversationId,
        int take,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns the oldest unsummarized messages that fall outside the most recent
    /// <paramref name="preserveRecentCount"/> messages. The checkpoint is exclusive.
    /// Implementations may return fewer than <paramref name="take"/> results.
    /// </summary>
    Task<IReadOnlyList<Message>> ListSummaryCandidatesAsync(
        Guid conversationId,
        int preserveRecentCount,
        DateTimeOffset? afterCreatedAt,
        Guid? afterId,
        int take,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<Message>>(Array.Empty<Message>());

    Task AddAsync(Message message, CancellationToken cancellationToken);
}
