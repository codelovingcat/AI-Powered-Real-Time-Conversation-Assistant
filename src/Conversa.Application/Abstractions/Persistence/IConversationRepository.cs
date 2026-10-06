using Conversa.Domain.Conversations;

namespace Conversa.Application.Abstractions.Persistence;

public interface IConversationRepository
{
    Task<Conversation?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Conversation>> ListByUserAsync(Guid userId, int take, CancellationToken cancellationToken);

    Task<IReadOnlyList<Conversation>> ListByUserPageAsync(
        Guid userId,
        int take,
        string? search,
        DateTimeOffset? updatedFrom,
        DateTimeOffset? updatedTo,
        DateTimeOffset? beforeUpdatedAt,
        Guid? beforeId,
        CancellationToken cancellationToken);

    Task AddAsync(Conversation conversation, CancellationToken cancellationToken);

    void Remove(Conversation conversation);
}
