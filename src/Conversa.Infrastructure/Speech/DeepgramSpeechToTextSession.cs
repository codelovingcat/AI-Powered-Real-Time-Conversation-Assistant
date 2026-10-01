using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Conversa.Application.Speech;

namespace Conversa.Infrastructure.Speech;

public sealed class DeepgramSpeechToTextSession : ISpeechToTextSession
{
    private readonly DeepgramOptions _settings;
    private readonly SpeechSessionOptions _sessionOptions;
    private readonly ClientWebSocket _socket = new();
    private readonly Channel<SpeechTranscriptUpdate> _updates =
        Channel.CreateBounded<SpeechTranscriptUpdate>(
            new BoundedChannelOptions(100)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
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
        var parameters = DeepgramSpeechToTextProvider.BuildParameters(
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
                        return;
                    }

                    if (received.MessageType != WebSocketMessageType.Text)
                    {
                        continue;
                    }

                    await message.WriteAsync(
                        buffer.AsMemory(0, received.Count),
                        cancellationToken);
                }
                while (!received.EndOfMessage);

                if (message.Length == 0)
                    continue;

                var json = Encoding.UTF8.GetString(message.ToArray());

                SpeechTranscriptUpdate? update;
                try
                {
                    update = DeepgramTranscriptParser.ParseStreamingResult(json);
                }
                catch (JsonException)
                {
                    update = new SpeechTranscriptUpdate(
                        string.Empty,
                        true,
                        null,
                        SpeechTranscriptStatus.Failed,
                        new SpeechError(
                            "invalid_provider_response",
                            "The speech-to-text provider returned an invalid response."));
                }

                if (update is not null)
                {
                    await _updates.Writer.WriteAsync(update, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (WebSocketException)
        {
            _updates.Writer.TryWrite(
                new SpeechTranscriptUpdate(
                    string.Empty,
                    true,
                    null,
                    SpeechTranscriptStatus.Failed,
                    new SpeechError(
                        "provider_unavailable",
                        "The speech-to-text provider streaming connection is unavailable.")));
        }
        finally
        {
            _updates.Writer.TryComplete();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        try
        {
            await _sendLock.WaitAsync();
            try
            {
                if (_socket.State == WebSocketState.Open)
                {
                    var closeMessage = Encoding.UTF8.GetBytes(
                        JsonSerializer.Serialize(new { type = "CloseStream" }));

                    await _socket.SendAsync(
                        closeMessage,
                        WebSocketMessageType.Text,
                        endOfMessage: true,
                        CancellationToken.None);

                    await _socket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "session complete",
                        CancellationToken.None);
                }
            }
            finally
            {
                _sendLock.Release();
            }

            _disposeCts.Cancel();

            if (_receiveTask is not null)
            {
                await _receiveTask;
            }
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
