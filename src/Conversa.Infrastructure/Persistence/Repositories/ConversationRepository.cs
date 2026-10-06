using Conversa.Application.Abstractions.Persistence;
using Conversa.Domain.Conversations;
using Microsoft.EntityFrameworkCore;

namespace Conversa.Infrastructure.Persistence.Repositories;

internal sealed class ConversationRepository(AppDbContext db) : IConversationRepository
{
    public Task<Conversation?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken)
        => db.Conversations.FirstOrDefaultAsync(
            conversation => conversation.Id == id && conversation.UserId == userId,
            cancellationToken);

    public async Task<IReadOnlyList<Conversation>> ListByUserAsync(
        Guid userId,
        int take,
        CancellationToken cancellationToken)
        => await db.Conversations
            .Where(conversation => conversation.UserId == userId)
            .OrderByDescending(conversation => conversation.UpdatedAt)
            .ThenByDescending(conversation => conversation.Id)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Conversation>> ListByUserPageAsync(
        Guid userId,
        int take,
        string? search,
        DateTimeOffset? updatedFrom,
        DateTimeOffset? updatedTo,
        DateTimeOffset? beforeUpdatedAt,
        Guid? beforeId,
        CancellationToken cancellationToken)
    {
        var query = db.Conversations
            .Where(conversation => conversation.UserId == userId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var escapedSearch = EscapeLikePattern(search.Trim());
            var pattern = $"%{escapedSearch}%";

            query = query.Where(conversation =>
                EF.Functions.ILike(conversation.Title, pattern, "\\\\")
                || EF.Functions.ILike(conversation.SourceLanguage, pattern, "\\\\")
                || EF.Functions.ILike(conversation.TargetLanguage, pattern, "\\\\"));
        }

        if (updatedFrom is not null)
        {
            query = query.Where(conversation => conversation.UpdatedAt >= updatedFrom.Value);
        }

        if (updatedTo is not null)
        {
            query = query.Where(conversation => conversation.UpdatedAt < updatedTo.Value);
        }

        if (beforeUpdatedAt is not null && beforeId is not null)
        {
            query = query.Where(conversation =>
                conversation.UpdatedAt < beforeUpdatedAt.Value
                || (conversation.UpdatedAt == beforeUpdatedAt.Value
                    && conversation.Id.CompareTo(beforeId.Value) < 0));
        }

        return await query
            .OrderByDescending(conversation => conversation.UpdatedAt)
            .ThenByDescending(conversation => conversation.Id)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    private static string EscapeLikePattern(string value)
        => value
            .Replace("\\\\", "\\\\\\\\", StringComparison.Ordinal)
            .Replace("%", "\\\\%", StringComparison.Ordinal)
            .Replace("_", "\\\\_", StringComparison.Ordinal);

    public async Task AddAsync(Conversation conversation, CancellationToken cancellationToken)
        => await db.Conversations.AddAsync(conversation, cancellationToken);

    public void Remove(Conversation conversation) => db.Conversations.Remove(conversation);
}
