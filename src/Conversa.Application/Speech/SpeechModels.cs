namespace Conversa.Application.Speech;

public sealed record SpeechAudio(
    ReadOnlyMemory<byte> Content,
    string ContentType,
    string Language);

public sealed record SpeechRecognitionResult(
    string Text,
    bool IsFinal,
    float? Confidence,
    SpeechRecognitionStatus Status = SpeechRecognitionStatus.Completed,
    SpeechError? Error = null);

public sealed record SpeechSessionOptions(
    Guid ConversationId,
    string Language,
    string? ContentType,
    int? SampleRateHertz);

public sealed record SpeechTranscriptUpdate(
    string Text,
    bool IsFinal,
    float? Confidence,
    SpeechTranscriptStatus Status = SpeechTranscriptStatus.Transcript,
    SpeechError? Error = null);

public enum SpeechRecognitionStatus
{
    Completed,
    Cancelled,
    Failed
}

public enum SpeechTranscriptStatus
{
    Transcript,
    Completed,
    Cancelled,
    Failed
}

public sealed record SpeechError(
    string Code,
    string Message);
