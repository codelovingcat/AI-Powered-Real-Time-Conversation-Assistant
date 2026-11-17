using System.Text.Json;
using Conversa.Application.Speech;

namespace Conversa.Infrastructure.Speech;

public static class DeepgramTranscriptParser
{
    public static SpeechTranscriptUpdate? ParseStreamingResult(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
            return InvalidStreamingResponse();

        if (root.TryGetProperty("type", out var typeElement)
            && typeElement.ValueKind == JsonValueKind.String
            && string.Equals(typeElement.GetString(), "Error", StringComparison.OrdinalIgnoreCase))
        {
            return FailedStreamingResponse(
                "provider_unavailable",
                "The speech-to-text provider could not process the audio stream.");
        }

        // Metadata and other non-transcript provider events are expected and ignored.
        if (!root.TryGetProperty("channel", out var channel))
            return null;

        if (channel.ValueKind != JsonValueKind.Object)
            return InvalidStreamingResponse();

        if (!channel.TryGetProperty("alternatives", out var alternatives)
            || alternatives.ValueKind != JsonValueKind.Array)
        {
            return InvalidStreamingResponse();
        }

        if (alternatives.GetArrayLength() == 0)
            return null;

        var alternative = alternatives[0];
        if (alternative.ValueKind != JsonValueKind.Object)
            return InvalidStreamingResponse();

        if (!alternative.TryGetProperty("transcript", out var transcriptElement)
            || transcriptElement.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
        {
            return InvalidStreamingResponse();
        }

        var transcript = transcriptElement.ValueKind == JsonValueKind.String
            ? transcriptElement.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(transcript))
            return null;

        var confidence = alternative.TryGetProperty("confidence", out var confidenceElement)
            && confidenceElement.ValueKind == JsonValueKind.Number
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
                new SpeechError("invalid_provider_response", "The speech provider returned no transcript channels."));
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
                new SpeechError("invalid_provider_response", "The speech provider returned no transcript alternatives."));
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

    private static SpeechTranscriptUpdate InvalidStreamingResponse()
    {
        return FailedStreamingResponse(
            "invalid_provider_response",
            "The speech-to-text provider returned an invalid response.");
    }

    private static SpeechTranscriptUpdate FailedStreamingResponse(
        string code,
        string message)
    {
        return new SpeechTranscriptUpdate(
            string.Empty,
            true,
            null,
            SpeechTranscriptStatus.Failed,
            new SpeechError(code, message));
    }
}
