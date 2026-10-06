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
            .ThenBy(message => message.Id)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Message>> ListByConversationPageAsync(
        Guid conversationId,
        int take,
        DateTimeOffset? beforeCreatedAt,
        Guid? beforeId,
        CancellationToken cancellationToken)
    {
        var query = db.Messages
            .Where(message => message.ConversationId == conversationId);

        if (beforeCreatedAt is not null && beforeId is not null)
        {
            query = query.Where(message =>
                message.CreatedAt < beforeCreatedAt.Value
                || (message.CreatedAt == beforeCreatedAt.Value
                    && message.Id.CompareTo(beforeId.Value) < 0));
        }

        return await query
            .OrderByDescending(message => message.CreatedAt)
            .ThenByDescending(message => message.Id)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

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
