using Conversa.Domain.Conversations;

namespace Conversa.Application.Abstractions.Persistence;

public interface IMessageRepository
{
    Task<IReadOnlyList<Message>> ListByConversationAsync(
        Guid conversationId,
        int take,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Message>> ListRecentAsync(
        Guid conversationId,
        int take,
        CancellationToken cancellationToken);

    Task AddAsync(Message message, CancellationToken cancellationToken);
}
