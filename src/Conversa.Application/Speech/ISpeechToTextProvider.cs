namespace Conversa.Application.Speech;

/// <summary>
/// One-shot transcription of a completed audio payload. No provider is registered yet.
/// </summary>
public interface ISpeechToTextProvider
{
    string ProviderName { get; }

    Task<SpeechRecognitionResult> TranscribeAsync(SpeechAudio audio, CancellationToken cancellationToken);
}
