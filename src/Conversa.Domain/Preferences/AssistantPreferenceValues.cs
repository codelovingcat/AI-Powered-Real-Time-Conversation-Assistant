namespace Conversa.Domain.Preferences;

public static class AssistantPreferenceValues
{
    public const string DefaultResponseLength = "balanced";
    public const string DefaultTone = "contextual";
    public const string DefaultExplanationLevel = "balanced";
    public const bool DefaultSuggestReplies = true;

    public static bool IsValidResponseLength(string? value)
        => value is "concise" or "balanced" or "detailed";

    public static bool IsValidTone(string? value)
        => value is "casual" or "contextual" or "formal";

    public static bool IsValidExplanationLevel(string? value)
        => value is "minimal" or "balanced" or "detailed";
}
