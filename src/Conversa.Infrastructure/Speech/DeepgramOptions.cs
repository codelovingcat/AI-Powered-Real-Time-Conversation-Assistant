namespace Conversa.Infrastructure.Speech;

public sealed class DeepgramOptions
{
    public const string SectionName = "Deepgram";

    public string ApiKey { get; init; } = string.Empty;
    public string Model { get; init; } = "nova-3";
    public string BaseUrl { get; init; } = "https://api.deepgram.com";
    public int EndpointingMilliseconds { get; init; } = 300;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException(
                "Deepgram:ApiKey is required when the Deepgram speech-to-text provider is enabled.");
        }

        if (string.IsNullOrWhiteSpace(Model))
            throw new InvalidOperationException("Deepgram:Model must be configured.");

        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var baseUri)
            || baseUri.Scheme is not ("https" or "http"))
        {
            throw new InvalidOperationException(
                "Deepgram:BaseUrl must be an absolute HTTP or HTTPS URI.");
        }

        if (EndpointingMilliseconds < 0)
        {
            throw new InvalidOperationException(
                "Deepgram:EndpointingMilliseconds must be zero or greater.");
        }
    }
}
