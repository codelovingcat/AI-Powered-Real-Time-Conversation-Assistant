namespace Conversa.Application.Speech;

/// <summary>
/// Streaming speech session. A future provider registers <see cref="ISpeechToTextSessionFactory"/>;
/// the WebSocket host already pumps chunks through this contract.
/// </summary>
public interface ISpeechToTextSessionFactory
{
    string ProviderName { get; }

    Task<ISpeechToTextSession> OpenSessionAsync(SpeechSessionOptions options, CancellationToken cancellationToken);
}

public interface ISpeechToTextSession : IAsyncDisposable
{
    ValueTask AppendAudioAsync(ReadOnlyMemory<byte> audioChunk, CancellationToken cancellationToken);

    IAsyncEnumerable<SpeechTranscriptUpdate> ReadUpdatesAsync(CancellationToken cancellationToken);
}
