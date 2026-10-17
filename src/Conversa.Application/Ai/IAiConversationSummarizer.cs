using Conversa.Domain.Conversations;

namespace Conversa.Application.Ai;

/// <summary>
/// Produces a bounded, cumulative memory for the older part of one conversation.
/// The active instruction and transcripts are inputs only; provider implementations
/// must never log them or use a summary from another conversation.
/// </summary>
public interface IAiConversationSummarizer
{
    Task<string> SummarizeAsync(
        string instruction,
        string sourceLanguage,
        string targetLanguage,
        string? existingSummary,
        IReadOnlyList<AiConversationTurn> olderTurns,
        CancellationToken cancellationToken);
}
