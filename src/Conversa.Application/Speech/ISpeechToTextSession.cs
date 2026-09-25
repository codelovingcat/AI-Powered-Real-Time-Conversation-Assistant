namespace Conversa.Application.Speech;

public interface ISpeechToTextSessionFactory
{
    string ProviderName { get; }

    Task<ISpeechToTextSession> OpenSessionAsync(
        SpeechSessionOptions options,
        CancellationToken cancellationToken);
}

public interface ISpeechToTextSession : IAsyncDisposable
{
    ValueTask AppendAudioAsync(
        ReadOnlyMemory<byte> audioChunk,
        CancellationToken cancellationToken);

    IAsyncEnumerable<SpeechTranscriptUpdate> ReadUpdatesAsync(
        CancellationToken cancellationToken);
}
