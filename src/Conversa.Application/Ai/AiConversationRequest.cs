namespace Conversa.Application.Ai;

public sealed record AiConversationRequest(
    string Instruction,
    string SourceLanguage,
    string TargetLanguage,
    string InputText,
    AiInputKind InputKind,
    IReadOnlyList<AiConversationTurn> RecentTurns);
