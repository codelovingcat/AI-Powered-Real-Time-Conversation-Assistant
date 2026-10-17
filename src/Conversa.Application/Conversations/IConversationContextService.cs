using Conversa.Domain.Conversations;

namespace Conversa.Application.Conversations;

/// <summary>
/// Refreshes a persisted summary from older transcript messages and returns the last committed summary.
/// A refresh failure must leave the last committed summary unchanged.
/// </summary>
public interface IConversationContextService
{
    Task<string?> PrepareContextSummaryAsync(
        Conversation conversation,
        IReadOnlyList<Message> recentMessages,
        CancellationToken cancellationToken);
}
