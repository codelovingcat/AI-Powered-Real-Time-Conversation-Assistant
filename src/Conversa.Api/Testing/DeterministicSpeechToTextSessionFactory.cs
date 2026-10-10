using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Conversa.Application.Speech;

namespace Conversa.Api.Testing;

/// <summary>
/// Emits synthetic partial and final transcripts for browser E2E tests only.
/// Never forwards audio to an external speech provider.
/// </summary>
internal sealed class DeterministicSpeechToTextSessionFactory
    : ISpeechToTextSessionFactory
{
    public string ProviderName => "e2e-test";

    public Task<ISpeechToTextSession> OpenSessionAsync(
        SpeechSessionOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<ISpeechToTextSession>(
            new DeterministicSpeechToTextSession());
    }

    private sealed class DeterministicSpeechToTextSession : ISpeechToTextSession
    {
        private readonly Channel<SpeechTranscriptUpdate> _updates =
            Channel.CreateUnbounded<SpeechTranscriptUpdate>();
        private int _hasEmittedTranscript;

        public async ValueTask AppendAudioAsync(
            ReadOnlyMemory<byte> audioChunk,
            CancellationToken cancellationToken)
        {
            if (audioChunk.IsEmpty ||
                Interlocked.Exchange(ref _hasEmittedTranscript, 1) != 0)
            {
                return;
            }

            await _updates.Writer.WriteAsync(
                new SpeechTranscriptUpdate(
                    "Could you tell me",
                    IsFinal: false,
                    Confidence: 0.98f),
                cancellationToken);

            await Task.Delay(TimeSpan.FromMilliseconds(900), cancellationToken);

            await _updates.Writer.WriteAsync(
                new SpeechTranscriptUpdate(
                    "Could you tell me about yourself?",
                    IsFinal: true,
                    Confidence: 0.99f),
                cancellationToken);
        }

        public async IAsyncEnumerable<SpeechTranscriptUpdate> ReadUpdatesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var update in _updates.Reader.ReadAllAsync(cancellationToken))
            {
                yield return update;
            }
        }

        public ValueTask DisposeAsync()
        {
            _updates.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }
}
