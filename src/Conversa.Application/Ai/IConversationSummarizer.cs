namespace Conversa.Application.Ai;

/// <summary>
/// Summarizes only a bounded, already ownership-scoped batch of older transcript turns.
/// Implementations must never log transcript text or return a summary that exceeds the domain limit.
/// </summary>
public interface IConversationSummarizer
{
    string PromptVersion { get; }

    Task<string> SummarizeAsync(
        string activeInstruction,
        string sourceLanguage,
        string targetLanguage,
        string? previousSummary,
        IReadOnlyList<AiConversationTurn> olderTurns,
        CancellationToken cancellationToken);
}
