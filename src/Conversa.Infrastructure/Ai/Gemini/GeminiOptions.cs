namespace Conversa.Infrastructure.Ai.Gemini;

public sealed class GeminiOptions
{
    public const string SectionName = "Gemini";

    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = "gemini-2.5-flash";

    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/";

    public int TimeoutSeconds { get; set; } = 45;

    public int MaxRetries { get; set; } = 2;

    public int InitialRetryDelayMilliseconds { get; set; } = 250;

    public void Validate()
    {
        if (TimeoutSeconds <= 0)
            throw new InvalidOperationException("Gemini:TimeoutSeconds must be greater than zero.");

        if (MaxRetries < 0 || MaxRetries > 3)
            throw new InvalidOperationException("Gemini:MaxRetries must be between 0 and 3.");

        if (InitialRetryDelayMilliseconds < 0 || InitialRetryDelayMilliseconds > 5000)
            throw new InvalidOperationException("Gemini:InitialRetryDelayMilliseconds must be between 0 and 5000.");
    }
}
