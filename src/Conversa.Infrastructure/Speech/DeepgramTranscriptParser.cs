using System.Text.Json;
using Conversa.Application.Speech;

namespace Conversa.Infrastructure.Speech;

public static class DeepgramTranscriptParser
{
    public static SpeechTranscriptUpdate? ParseStreamingResult(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (!root.TryGetProperty("channel", out var channel))
            return null;

        if (!channel.TryGetProperty("alternatives", out var alternatives)
            || alternatives.ValueKind != JsonValueKind.Array
            || alternatives.GetArrayLength() == 0)
        {
            return null;
        }

        var alternative = alternatives[0];

        var transcript = alternative.TryGetProperty("transcript", out var transcriptElement)
            ? transcriptElement.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(transcript))
            return null;

        var confidence = alternative.TryGetProperty("confidence", out var confidenceElement)
            && confidenceElement.ValueKind is JsonValueKind.Number
            && confidenceElement.TryGetSingle(out var parsedConfidence)
            ? parsedConfidence
            : (float?)null;

        var isFinal = root.TryGetProperty("is_final", out var isFinalElement)
            && isFinalElement.ValueKind == JsonValueKind.True;

        return new SpeechTranscriptUpdate(
            transcript,
            isFinal,
            confidence,
            root.TryGetProperty("speech_final", out var speechFinalElement)
                && speechFinalElement.ValueKind == JsonValueKind.True
                ? SpeechTranscriptStatus.Completed
                : SpeechTranscriptStatus.Transcript);
    }

    public static SpeechRecognitionResult ParsePreRecordedResult(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (!root.TryGetProperty("results", out var results)
            || !results.TryGetProperty("channels", out var channels)
            || channels.ValueKind != JsonValueKind.Array
            || channels.GetArrayLength() == 0)
        {
            return new SpeechRecognitionResult(
                string.Empty,
                true,
                null,
                SpeechRecognitionStatus.Failed,
                new SpeechError("invalid_provider_response", "Deepgram returned no transcript channels."));
        }

        var channel = channels[0];

        if (!channel.TryGetProperty("alternatives", out var alternatives)
            || alternatives.ValueKind != JsonValueKind.Array
            || alternatives.GetArrayLength() == 0)
        {
            return new SpeechRecognitionResult(
                string.Empty,
                true,
                null,
                SpeechRecognitionStatus.Failed,
                new SpeechError("invalid_provider_response", "Deepgram returned no transcript alternatives."));
        }

        var alternative = alternatives[0];

        var transcript = alternative.TryGetProperty("transcript", out var transcriptElement)
            ? transcriptElement.GetString() ?? string.Empty
            : string.Empty;

        var confidence = alternative.TryGetProperty("confidence", out var confidenceElement)
            && confidenceElement.ValueKind == JsonValueKind.Number
            && confidenceElement.TryGetSingle(out var parsedConfidence)
            ? parsedConfidence
            : (float?)null;

        return new SpeechRecognitionResult(
            transcript,
            true,
            confidence,
            SpeechRecognitionStatus.Completed);
    }
}
