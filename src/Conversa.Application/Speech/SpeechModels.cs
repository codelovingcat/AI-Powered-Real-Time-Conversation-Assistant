namespace Conversa.Application.Speech;

public sealed record SpeechAudio(ReadOnlyMemory<byte> Content, string ContentType, string Language);

public sealed record SpeechRecognitionResult(string Text, bool IsFinal, float? Confidence);

public sealed record SpeechSessionOptions(
    Guid ConversationId,
    string Language,
    string? ContentType,
    int? SampleRateHertz);

public sealed record SpeechTranscriptUpdate(string Text, bool IsFinal, float? Confidence);
