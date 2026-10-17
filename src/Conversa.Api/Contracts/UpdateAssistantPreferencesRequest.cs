namespace Conversa.Api.Contracts;

public sealed record UpdateAssistantPreferencesRequest(
    string? ResponseLength,
    string? Tone,
    string? ExplanationLevel,
    bool? SuggestReplies);
