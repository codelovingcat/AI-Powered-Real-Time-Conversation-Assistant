using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Conversa.Application.Observability;
using Conversa.Application.Speech;

namespace Conversa.Infrastructure.Speech;

public sealed class DeepgramSpeechToTextSession : ISpeechToTextSession
{
    private const int MaxProviderMessageBytes = 256 * 1024;

    private readonly DeepgramOptions _settings;
    private readonly SpeechSessionOptions _sessionOptions;
    private readonly ClientWebSocket _socket = new();
    private readonly Channel<SpeechTranscriptUpdate> _updates =
        Channel.CreateBounded<SpeechTranscriptUpdate>(
            new BoundedChannelOptions(100)
            {
                // Backpressure keeps the application bounded without silently dropping final
                // transcript events when a consumer is temporarily slower than the provider.
                FullMode = BoundedChannelFullMode.Wait,
                SingleWriter = true,
                SingleReader = false
            });
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _disposeCts = new();

    private Task? _receiveTask;
    private int _disposed;

    public DeepgramSpeechToTextSession(
        DeepgramOptions settings,
        SpeechSessionOptions sessionOptions)
    {
        _settings = settings;
        _sessionOptions = sessionOptions;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var activity = ConversaTelemetry.ActivitySource.StartActivity(
            "conversa.stt.streaming.start");
        var stopwatch = Stopwatch.StartNew();
        var outcome = "error";
        activity?.SetTag("conversa.provider", "deepgram");
        activity?.SetTag("conversa.operation", "stt.streaming.start");

        try
        {
            var parameters = DeepgramSpeechToTextProvider.BuildStreamingParameters(
                _settings,
                _sessionOptions.Language,
                _sessionOptions.ContentType,
                _sessionOptions.SampleRateHertz);

            var uri = DeepgramSpeechToTextProvider.BuildUri(
                _settings.BaseUrl.Replace("https://", "wss://", StringComparison.OrdinalIgnoreCase)
                    .Replace("http://", "ws://", StringComparison.OrdinalIgnoreCase),
                "/v1/listen",
                parameters);

            _socket.Options.SetRequestHeader("Authorization", $"Token {_settings.ApiKey}");

            try
            {
                await _socket.ConnectAsync(uri, cancellationToken);
            }
            catch
            {
                _socket.Dispose();
                throw;
            }

            _receiveTask = ReceiveLoopAsync(_disposeCts.Token);
            outcome = "success";
            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        catch (OperationCanceledException)
        {
            outcome = "cancelled";
            activity?.SetStatus(ActivityStatusCode.Error, "cancelled");
            throw;
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            throw;
        }
        finally
        {
            stopwatch.Stop();
            ConversaTelemetry.SttRequests.Add(
                1,
                ConversaTelemetry.Tags("deepgram", "streaming.start", outcome));
            ConversaTelemetry.SttDuration.Record(
                stopwatch.Elapsed.TotalMilliseconds,
                ConversaTelemetry.Tags("deepgram", "streaming.start", outcome));
            activity?.SetTag("conversa.outcome", outcome);
        }
    }

    public async ValueTask AppendAudioAsync(
        ReadOnlyMemory<byte> audioChunk,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        if (audioChunk.IsEmpty)
            return;

        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            await _socket.SendAsync(
                audioChunk,
                WebSocketMessageType.Binary,
                endOfMessage: true,
                cancellationToken);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public IAsyncEnumerable<SpeechTranscriptUpdate> ReadUpdatesAsync(
        CancellationToken cancellationToken)
    {
        return _updates.Reader.ReadAllAsync(cancellationToken);
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];

        try
        {
            while (_socket.State is WebSocketState.Open or WebSocketState.CloseSent)
            {
                using var message = new MemoryStream();

                WebSocketReceiveResult received;
                do
                {
                    received = await _socket.ReceiveAsync(buffer, cancellationToken);

                    if (received.MessageType == WebSocketMessageType.Close)
                    {
                        if (!cancellationToken.IsCancellationRequested)
                        {
                            await PublishFailureAsync(
                                "provider_unavailable",
                                "The speech-to-text provider streaming connection closed unexpectedly.",
                                cancellationToken);
                        }

                        return;
                    }

                    if (received.MessageType != WebSocketMessageType.Text)
                    {
                        await PublishFailureAsync(
                            "invalid_provider_response",
                            "The speech-to-text provider returned an unsupported message.",
                            cancellationToken);
                        return;
                    }

                    if (message.Length + received.Count > MaxProviderMessageBytes)
                    {
                        await PublishFailureAsync(
                            "invalid_provider_response",
                            "The speech-to-text provider response exceeded the allowed size.",
                            cancellationToken);
                        return;
                    }

                    await message.WriteAsync(
                        buffer.AsMemory(0, received.Count),
                        cancellationToken);
                }
                while (!received.EndOfMessage);

                if (message.Length == 0)
                    continue;

                var json = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);

                SpeechTranscriptUpdate? update;
                try
                {
                    update = DeepgramTranscriptParser.ParseStreamingResult(json);
                }
                catch (JsonException)
                {
                    update = FailedUpdate(
                        "invalid_provider_response",
                        "The speech-to-text provider returned an invalid response.");
                }
                catch (InvalidOperationException)
                {
                    update = FailedUpdate(
                        "invalid_provider_response",
                        "The speech-to-text provider returned an invalid response.");
                }

                if (update is not null)
                {
                    await _updates.Writer.WriteAsync(update, cancellationToken);

                    if (update.Status is SpeechTranscriptStatus.Failed or SpeechTranscriptStatus.Cancelled)
                        return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (WebSocketException)
        {
            await PublishFailureAsync(
                "provider_unavailable",
                "The speech-to-text provider streaming connection is unavailable.",
                CancellationToken.None);
        }
        catch (Exception)
        {
            await PublishFailureAsync(
                "transcription_failed",
                "Speech recognition could not complete this audio stream.",
                CancellationToken.None);
        }
        finally
        {
            _updates.Writer.TryComplete();
        }
    }

    private async ValueTask PublishFailureAsync(
        string code,
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            await _updates.Writer.WriteAsync(
                FailedUpdate(code, message),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ChannelClosedException)
        {
        }
    }

    private static SpeechTranscriptUpdate FailedUpdate(string code, string message)
    {
        return new SpeechTranscriptUpdate(
            string.Empty,
            true,
            null,
            SpeechTranscriptStatus.Failed,
            new SpeechError(code, message));
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        try
        {
            using var closeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var lockTaken = false;

            try
            {
                await _sendLock.WaitAsync(closeTimeout.Token);
                lockTaken = true;

                if (_socket.State == WebSocketState.Open)
                {
                    var closeMessage = Encoding.UTF8.GetBytes(
                        JsonSerializer.Serialize(new { type = "CloseStream" }));

                    await _socket.SendAsync(
                        closeMessage,
                        WebSocketMessageType.Text,
                        endOfMessage: true,
                        closeTimeout.Token);

                    await _socket.CloseOutputAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "session complete",
                        closeTimeout.Token);
                }
            }
            finally
            {
                if (lockTaken)
                    _sendLock.Release();
            }
        }
        catch
        {
            _socket.Abort();
        }
        finally
        {
            _disposeCts.Cancel();
        }

        try
        {
            if (_receiveTask is not null)
                await _receiveTask.WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch
        {
            _socket.Abort();
        }
        finally
        {
            _socket.Dispose();
            _sendLock.Dispose();
            _disposeCts.Dispose();
            _updates.Writer.TryComplete();
        }
    }
}
