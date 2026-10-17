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
            .ThenByDescending(message => message.Id)
            .Take(take)
            .ToListAsync(cancellationToken);

        recent.Reverse();
        return recent;
    }

    public async Task<IReadOnlyList<Message>> ListSummaryCandidatesAsync(
        Guid conversationId,
        int preserveRecentCount,
        DateTimeOffset? afterCreatedAt,
        Guid? afterId,
        int take,
        CancellationToken cancellationToken)
    {
        if (preserveRecentCount < 0)
            throw new ArgumentOutOfRangeException(nameof(preserveRecentCount));
        if (take <= 0)
            throw new ArgumentOutOfRangeException(nameof(take));
        if (afterCreatedAt.HasValue != afterId.HasValue)
            throw new ArgumentException("The summary checkpoint timestamp and message id must be supplied together.");

        // Find the 13th (or configured N+1) newest message. Everything at or
        // before this pair is eligible; the newest N turns remain verbatim.
        var cutoff = await db.Messages
            .Where(message => message.ConversationId == conversationId)
            .OrderByDescending(message => message.CreatedAt)
            .ThenByDescending(message => message.Id)
            .Skip(preserveRecentCount)
            .Select(message => new { message.CreatedAt, message.Id })
            .FirstOrDefaultAsync(cancellationToken);

        if (cutoff is null)
            return Array.Empty<Message>();

        var query = db.Messages.Where(message =>
            message.ConversationId == conversationId
            && (message.CreatedAt < cutoff.CreatedAt
                || (message.CreatedAt == cutoff.CreatedAt
                    && message.Id.CompareTo(cutoff.Id) <= 0)));

        if (afterCreatedAt is { } checkpointAt && afterId is { } checkpointId)
        {
            query = query.Where(message =>
                message.CreatedAt > checkpointAt
                || (message.CreatedAt == checkpointAt
                    && message.Id.CompareTo(checkpointId) > 0));
        }

        return await query
            .OrderBy(message => message.CreatedAt)
            .ThenBy(message => message.Id)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(Message message, CancellationToken cancellationToken)
        => await db.Messages.AddAsync(message, cancellationToken);
}
