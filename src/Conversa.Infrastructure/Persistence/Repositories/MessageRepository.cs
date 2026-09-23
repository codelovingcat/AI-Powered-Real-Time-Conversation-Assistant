using Conversa.Application.Abstractions.Persistence;
using Conversa.Domain.Conversations;
using Microsoft.EntityFrameworkCore;

namespace Conversa.Infrastructure.Persistence.Repositories;

internal sealed class MessageRepository(AppDbContext db) : IMessageRepository
{
    public async Task<IReadOnlyList<Message>> ListByConversationAsync(
        Guid conversationId,
        int take,
        CancellationToken cancellationToken)
        => await db.Messages
            .Where(message => message.ConversationId == conversationId)
            .OrderBy(message => message.CreatedAt)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Message>> ListRecentAsync(
        Guid conversationId,
        int take,
        CancellationToken cancellationToken)
    {
        var recent = await db.Messages
            .Where(message => message.ConversationId == conversationId)
            .OrderByDescending(message => message.CreatedAt)
            .Take(take)
            .ToListAsync(cancellationToken);

        recent.Reverse();
        return recent;
    }

    public async Task AddAsync(Message message, CancellationToken cancellationToken)
        => await db.Messages.AddAsync(message, cancellationToken);
}
