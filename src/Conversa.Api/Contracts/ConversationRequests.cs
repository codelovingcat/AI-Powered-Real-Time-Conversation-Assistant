using Conversa.Application.Ai;

namespace Conversa.Api.Contracts;

public sealed class CreateConversationRequest
{
    public string? Title { get; init; }

    public string Instruction { get; init; } = string.Empty;

    public string? SourceLanguage { get; init; }

    public string? TargetLanguage { get; init; }
}

public sealed class UpdateConversationRequest
{
    public string? Title { get; init; }

    public string? Instruction { get; init; }

    public string? SourceLanguage { get; init; }

    public string? TargetLanguage { get; init; }
}

public sealed class ProcessInputRequest
{
    public AiInputKind? InputKind { get; init; }

    public string Text { get; init; } = string.Empty;
}
