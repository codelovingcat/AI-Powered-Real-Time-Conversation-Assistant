namespace Conversa.Domain.Preferences;

public sealed class UserAssistantPreferences
{
    private UserAssistantPreferences()
    {
    }

    public Guid UserId { get; private set; }

    public string ResponseLength { get; private set; } = AssistantPreferenceValues.DefaultResponseLength;

    public string Tone { get; private set; } = AssistantPreferenceValues.DefaultTone;

    public string ExplanationLevel { get; private set; } = AssistantPreferenceValues.DefaultExplanationLevel;

    public bool SuggestReplies { get; private set; } = AssistantPreferenceValues.DefaultSuggestReplies;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static UserAssistantPreferences Create(
        Guid userId,
        string responseLength,
        string tone,
        string explanationLevel,
        bool suggestReplies,
        DateTimeOffset now)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User id is required.", nameof(userId));
        }

        Validate(responseLength, tone, explanationLevel);

        return new UserAssistantPreferences
        {
            UserId = userId,
            ResponseLength = responseLength,
            Tone = tone,
            ExplanationLevel = explanationLevel,
            SuggestReplies = suggestReplies,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void Update(
        string responseLength,
        string tone,
        string explanationLevel,
        bool suggestReplies,
        DateTimeOffset now)
    {
        Validate(responseLength, tone, explanationLevel);

        ResponseLength = responseLength;
        Tone = tone;
        ExplanationLevel = explanationLevel;
        SuggestReplies = suggestReplies;
        UpdatedAt = now;
    }

    private static void Validate(
        string responseLength,
        string tone,
        string explanationLevel)
    {
        if (!AssistantPreferenceValues.IsValidResponseLength(responseLength))
        {
            throw new ArgumentException("Unsupported response length.", nameof(responseLength));
        }

        if (!AssistantPreferenceValues.IsValidTone(tone))
        {
            throw new ArgumentException("Unsupported tone.", nameof(tone));
        }

        if (!AssistantPreferenceValues.IsValidExplanationLevel(explanationLevel))
        {
            throw new ArgumentException("Unsupported explanation level.", nameof(explanationLevel));
        }
    }
}
