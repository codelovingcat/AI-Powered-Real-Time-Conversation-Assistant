using Conversa.Application.Speech;

namespace Conversa.Infrastructure.Speech;

internal static class DeepgramAudioFormat
{
    public static void ApplyQueryParameters(
        List<KeyValuePair<string, string>> parameters,
        string? contentType,
        int? sampleRateHertz)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return;
        }

        var mediaType = contentType
            .Split(';', 2, StringSplitOptions.TrimEntries)[0]
            .Trim()
            .ToLowerInvariant();

        if (mediaType is "audio/raw" or "audio/pcm" or "audio/l16")
        {
            if (sampleRateHertz is null or <= 0)
            {
                throw new InvalidOperationException(
                    "A positive sample rate is required when streaming raw PCM audio to Deepgram.");
            }

            parameters.Add(new("encoding", "linear16"));
            parameters.Add(new("sample_rate", sampleRateHertz.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
    }

    public static string GetContentType(SpeechAudio audio)
    {
        return string.IsNullOrWhiteSpace(audio.ContentType)
            ? "application/octet-stream"
            : audio.ContentType;
    }
}
