using Conversa.Application.Abstractions.Persistence;
using Conversa.Application.Ai;
using Conversa.Application.Common;
using Conversa.Domain.Conversations;
using Microsoft.Extensions.Logging;

namespace Conversa.Application.Conversations;

/// <summary>
/// Incrementally summarizes bounded batches of older transcript messages.
/// The persisted summary is changed only after a complete, validated summary has been generated.
/// </summary>
public sealed class ConversationContextService(
    IConversationContextSummaryRepository summaries,
    IConversationSummarizer summarizer,
    TimeProvider timeProvider,
    ILogger<ConversationContextService> logger) : IConversationContextService
{
    public const int SummaryBatchSize = 8;

    public async Task<string?> PrepareContextSummaryAsync(
        Conversation conversation,
        IReadOnlyList<Message> recentMessages,
        CancellationToken cancellationToken)
    {
        var current = await summaries.GetByConversationIdAsync(
            conversation.Id,
            conversation.UserId,
            cancellationToken);

        if (recentMessages.Count == 0)
        {
            return current?.Summary;
        }

        // Recent messages are supplied oldest-first. Their first entry is the exclusive
        // boundary: these messages remain verbatim in the normal AI request.
        var oldestRecent = recentMessages[0];
        var pending = await summaries.ListUnsummarizedMessagesAsync(
            conversation.Id,
            conversation.UserId,
            current?.ThroughCreatedAt,
            current?.ThroughMessageId,
            oldestRecent.CreatedAt,
            oldestRecent.Id,
            SummaryBatchSize,
            cancellationToken);

        if (pending.Count < SummaryBatchSize)
        {
            return current?.Summary;
        }

        try
        {
            var olderTurns = pending.Select(message => new AiConversationTurn(
                message.Role,
                message.OriginalText,
                message.Translation,
                message.SuggestedAnswer,
                message.CreatedAt)).ToArray();

            var refreshed = await summarizer.SummarizeAsync(
                conversation.Instruction,
                conversation.SourceLanguage,
                conversation.TargetLanguage,
                current?.Summary,
                olderTurns,
                cancellationToken);

            if (string.IsNullOrWhiteSpace(refreshed)
                || refreshed.Trim().Length > ConversationContextSummary.SummaryMaxLength)
            {
                throw new AiProviderException("Conversation summarization returned an invalid summary.");
            }

            var through = pending[^1];
            var saved = await summaries.TrySaveAsync(
                conversation.Id,
                conversation.UserId,
                current?.Revision ?? 0,
                refreshed.Trim(),
                through.CreatedAt,
                through.Id,
                timeProvider.GetUtcNow(),
                cancellationToken);

            if (saved)
            {
                return refreshed.Trim();
            }

            // Another request won the optimistic-concurrency race. Prefer its committed,
            // newer summary rather than overwriting it with this stale result.
            var latest = await summaries.GetByConversationIdAsync(
                conversation.Id,
                conversation.UserId,
                cancellationToken);
            return latest?.Summary ?? current?.Summary;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // No summary columns are written until generation and validation have succeeded.
            // Log only the exception type; never log transcript or summary text.
            logger.LogWarning(
                "Conversation context summary refresh failed; the last committed summary remains in use. ErrorType {ErrorType}.",
                exception.GetType().Name);
            return current?.Summary;
        }
    }
}
