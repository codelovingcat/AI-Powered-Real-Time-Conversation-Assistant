namespace Conversa.Api.Realtime;

public sealed class AudioWebSocketOptions
{
    public const string SectionName = "AudioWebSocket";

    public int MaxMessageBytes { get; init; } = 256 * 1024;
    public int MaxConnectionSeconds { get; init; } = 10 * 60;

    public void Validate()
    {
        if (MaxMessageBytes <= 0)
            throw new InvalidOperationException("AudioWebSocket:MaxMessageBytes must be greater than zero.");

        if (MaxConnectionSeconds <= 0)
            throw new InvalidOperationException("AudioWebSocket:MaxConnectionSeconds must be greater than zero.");
    }
}
