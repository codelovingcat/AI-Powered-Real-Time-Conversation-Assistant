using Conversa.Application.Abstractions.Persistence;
using Conversa.Infrastructure.Persistence;
using Conversa.Domain.Conversations;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Conversa.Infrastructure.Persistence.Repositories;

internal sealed class ConversationContextSummaryRepository(AppDbContext db)
    : IConversationContextSummaryRepository
{
    public Task<ConversationContextSummary?> GetByConversationIdAsync(
        Guid conversationId,
        Guid userId,
        CancellationToken cancellationToken)
        => db.ConversationContextSummaries
            .AsNoTracking()
            .Where(summary =>
                summary.ConversationId == conversationId
                && db.Conversations.Any(conversation =>
                    conversation.Id == summary.ConversationId
                    && conversation.UserId == userId))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<Message>> ListUnsummarizedMessagesAsync(
        Guid conversationId,
        Guid userId,
        DateTimeOffset? afterCreatedAt,
        Guid? afterMessageId,
        DateTimeOffset beforeCreatedAt,
        Guid beforeMessageId,
        int take,
        CancellationToken cancellationToken)
    {
        if (take <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(take), "Take must be greater than zero.");
        }

        var boundedTake = Math.Min(take, 64);
        var query = db.Messages
            .AsNoTracking()
            .Where(message =>
                message.ConversationId == conversationId
                && db.Conversations.Any(conversation =>
                    conversation.Id == message.ConversationId
                    && conversation.UserId == userId));

        if (afterCreatedAt is not null && afterMessageId is not null)
        {
            query = query.Where(message =>
                message.CreatedAt > afterCreatedAt.Value
                || (message.CreatedAt == afterCreatedAt.Value
                    && message.Id.CompareTo(afterMessageId.Value) > 0));
        }

        query = query.Where(message =>
            message.CreatedAt < beforeCreatedAt
            || (message.CreatedAt == beforeCreatedAt
                && message.Id.CompareTo(beforeMessageId) < 0));

        return await query
            .OrderBy(message => message.CreatedAt)
            .ThenBy(message => message.Id)
            .Take(boundedTake)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> TrySaveAsync(
        Guid conversationId,
        Guid userId,
        int expectedRevision,
        string summary,
        DateTimeOffset throughCreatedAt,
        Guid throughMessageId,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        var normalizedSummary = summary.Trim();
        if (normalizedSummary.Length > ConversationContextSummary.SummaryMaxLength)
        {
            throw new ArgumentException(
                $"Conversation summary cannot exceed {ConversationContextSummary.SummaryMaxLength} characters.",
                nameof(summary));
        }

        if (expectedRevision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        }

        if (!await db.Conversations.AnyAsync(
                conversation => conversation.Id == conversationId && conversation.UserId == userId,
                cancellationToken))
        {
            return false;
        }

        if (expectedRevision == 0)
        {
            var record = ConversationContextSummary.Create(
                conversationId,
                normalizedSummary,
                throughCreatedAt,
                throughMessageId,
                updatedAt);
            db.ConversationContextSummaries.Add(record);

            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return true;
            }
            catch (DbUpdateException exception) when (IsUniqueViolation(exception))
            {
                db.Entry(record).State = EntityState.Detached;
                return false;
            }
            catch
            {
                db.Entry(record).State = EntityState.Detached;
                throw;
            }
        }

        var affected = await db.ConversationContextSummaries
            .Where(record =>
                record.ConversationId == conversationId
                && record.Revision == expectedRevision
                && db.Conversations.Any(conversation =>
                    conversation.Id == record.ConversationId
                    && conversation.UserId == userId)
                && (record.ThroughCreatedAt < throughCreatedAt
                    || (record.ThroughCreatedAt == throughCreatedAt
                        && record.ThroughMessageId.CompareTo(throughMessageId) < 0)))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(record => record.Summary, normalizedSummary)
                    .SetProperty(record => record.ThroughCreatedAt, throughCreatedAt)
                    .SetProperty(record => record.ThroughMessageId, throughMessageId)
                    .SetProperty(record => record.UpdatedAt, updatedAt)
                    .SetProperty(record => record.Revision, record => record.Revision + 1),
                cancellationToken);

        return affected == 1;
    }

    private static bool IsUniqueViolation(DbUpdateException exception)
        => exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation
        };
}
