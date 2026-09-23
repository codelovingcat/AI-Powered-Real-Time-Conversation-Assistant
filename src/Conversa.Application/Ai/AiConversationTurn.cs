using Conversa.Domain.Conversations;

namespace Conversa.Application.Ai;

public sealed record AiConversationTurn(
    MessageRole Role,
    string OriginalText,
    string? Translation,
    string? SuggestedAnswer,
    DateTimeOffset CreatedAt);
