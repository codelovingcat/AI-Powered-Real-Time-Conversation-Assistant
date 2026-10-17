namespace Conversa.Application.Ai;

public sealed record AssistantResponsePreferences(
    string ResponseLength,
    string Tone,
    string ExplanationLevel,
    bool SuggestReplies)
{
    public static AssistantResponsePreferences Default { get; } = new(
        "balanced",
        "contextual",
        "balanced",
        true);
}
