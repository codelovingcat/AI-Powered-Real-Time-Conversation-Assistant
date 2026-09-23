using Conversa.Application.Ai;

namespace Conversa.Application.Conversations;

public sealed record CreateConversationCommand(
    string Title,
    string Instruction,
    string SourceLanguage,
    string TargetLanguage);

public sealed record UpdateConversationCommand(
    Guid ConversationId,
    string? Title,
    string? Instruction,
    string? SourceLanguage,
    string? TargetLanguage);

public sealed record ProcessConversationInputCommand(
    Guid ConversationId,
    AiInputKind InputKind,
    string Text);
