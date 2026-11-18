using System.Diagnostics;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Conversa.Application.Abstractions.Persistence;
using Conversa.Application.Observability;
using Conversa.Application.Speech;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Conversa.Api.Realtime;

public static class AudioWebSocketEndpoint
{
    public static void MapAudioWebSocket(this WebApplication app)
    {
        app.Map("/ws/conversations/{conversationId:guid}/audio", HandleAsync)
            .RequireRateLimiting("audio");
    }

    private static async Task HandleAsync(
        HttpContext context,
        Guid conversationId,
        IConversationRepository conversations,
        IServiceProvider services,
        IOptions<AudioWebSocketOptions> options)
    {
        using var activity = ConversaTelemetry.ActivitySource.StartActivity(
            "conversa.websocket.audio");
        var stopwatch = Stopwatch.StartNew();
        var outcome = "error";
        var connectionMetricRecorded = false;

        activity?.SetTag("conversa.operation", "audio.websocket");

        try
        {
            if (context.User.Identity?.IsAuthenticated != true)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            var rawUserId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? context.User.FindFirstValue("sub");

            if (!Guid.TryParse(rawUserId, out var userId) || userId == Guid.Empty)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            var conversation = await conversations.GetByIdAsync(
                conversationId,
                userId,
                context.RequestAborted);

            if (conversation is null)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            if (conversation.LifecycleState != Conversa.Domain.Conversations.ConversationLifecycleState.Active)
            {
                context.Response.StatusCode = StatusCodes.Status409Conflict;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "conversation_not_active",
                    message = "Start or resume the conversation before sending audio."
                }, context.RequestAborted);
                return;
            }

            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "websocket_required",
                    message = "This endpoint accepts a WebSocket upgrade."
                }, context.RequestAborted);
                return;
            }

            var settings = options.Value;
            settings.Validate();

            using var lifetimeCts = CancellationTokenSource.CreateLinkedTokenSource(
                context.RequestAborted);
            lifetimeCts.CancelAfter(TimeSpan.FromSeconds(settings.MaxConnectionSeconds));
            var cancellationToken = lifetimeCts.Token;

            var factory = services.GetService<ISpeechToTextSessionFactory>();
            var useAuthSubProtocol = WebSocketAuthentication.RequestedSubProtocol(
                context.Request.Headers);

            using var socket = useAuthSubProtocol
                ? await context.WebSockets.AcceptWebSocketAsync(WebSocketAuthentication.SubProtocol)
                : await context.WebSockets.AcceptWebSocketAsync();

            ConversaTelemetry.WebSocketConnections.Add(1);
            connectionMetricRecorded = true;

            if (factory is null)
            {
                await SendAsync(socket, new
                {
                    type = "error",
                    code = "stt_provider_not_configured",
                    message = "Speech recognition is not configured. Audio was not transcribed."
                }, cancellationToken);

                await CloseOutputSafelyAsync(
                    socket,
                    WebSocketCloseStatus.PolicyViolation,
                    "stt_provider_not_configured",
                    cancellationToken);
                outcome = "error";
                return;
            }

            ISpeechToTextSession session;
            try
            {
                session = await factory.OpenSessionAsync(
                    new SpeechSessionOptions(
                        conversation.Id,
                        conversation.SourceLanguage,
                        "audio/raw",
                        16000),
                    cancellationToken);
            }
            catch (OperationCanceledException) when (lifetimeCts.IsCancellationRequested)
            {
                outcome = "cancelled";
                await CloseOutputSafelyAsync(
                    socket,
                    WebSocketCloseStatus.PolicyViolation,
                    "connection cancelled",
                    CancellationToken.None);
                return;
            }
            catch (Exception exception)
            {
                activity?.SetTag("conversa.error_type", exception.GetType().Name);
                await SendAsync(socket, new
                {
                    type = "error",
                    code = "speech_provider_unavailable",
                    message = "Speech recognition is temporarily unavailable. Reconnect and try again."
                }, cancellationToken);

                await CloseOutputSafelyAsync(
                    socket,
                    WebSocketCloseStatus.PolicyViolation,
                    "speech_provider_unavailable",
                    cancellationToken);
                outcome = "error";
                return;
            }

            using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            var sessionToken = sessionCts.Token;
            var pumping = PumpTranscriptsAsync(socket, session, sessionToken);
            var receiving = ReceiveAudioAsync(socket, session, settings, sessionToken);
            var sessionDisposed = false;

            try
            {
                var firstCompleted = await Task.WhenAny(receiving, pumping);

                if (firstCompleted == pumping && !receiving.IsCompleted)
                {
                    // A provider-side failure or disconnect must not leave the client receive
                    // loop waiting indefinitely on an otherwise-open WebSocket.
                    sessionCts.Cancel();
                    await pumping;

                    try
                    {
                        await receiving;
                    }
                    catch (OperationCanceledException) when (sessionCts.IsCancellationRequested)
                    {
                    }

                    outcome = "error";
                }
                else
                {
                    await receiving;
                    await session.DisposeAsync();
                    sessionDisposed = true;
                    await pumping;
                    outcome = "success";
                    activity?.SetStatus(ActivityStatusCode.Ok);
                }
            }
            catch (OperationCanceledException) when (lifetimeCts.IsCancellationRequested)
            {
                outcome = "cancelled";
                activity?.SetStatus(ActivityStatusCode.Error, "cancelled");
                await CloseOutputSafelyAsync(
                    socket,
                    WebSocketCloseStatus.PolicyViolation,
                    "connection lifetime exceeded or request cancelled",
                    CancellationToken.None);
            }
            catch (OperationCanceledException) when (sessionCts.IsCancellationRequested)
            {
                outcome = "error";
            }
            catch (Exception exception)
            {
                outcome = "error";
                activity?.SetTag("conversa.error_type", exception.GetType().Name);
                activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            }
            finally
            {
                sessionCts.Cancel();

                if (!sessionDisposed)
                {
                    try
                    {
                        await session.DisposeAsync();
                    }
                    catch
                    {
                        // Cleanup failures must not leak provider details to the client.
                    }
                }

                try
                {
                    await pumping;
                }
                catch
                {
                    // The client has already received a provider-neutral error or disconnected.
                }
            }
        }
        catch (Exception exception)
        {
            outcome = "error";
            activity?.SetTag("conversa.error_type", exception.GetType().Name);
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            throw;
        }
        finally
        {
            stopwatch.Stop();

            if (connectionMetricRecorded)
            {
                ConversaTelemetry.WebSocketConnections.Add(-1);
            }

            ConversaTelemetry.WebSocketDuration.Record(
                stopwatch.Elapsed.TotalMilliseconds,
                new TagList { { "outcome", outcome } });

            if (outcome == "error")
            {
                ConversaTelemetry.WebSocketFailures.Add(1);
            }

            activity?.SetTag("conversa.outcome", outcome);
        }
    }

    private static async Task ReceiveAudioAsync(
        WebSocket socket,
        ISpeechToTextSession session,
        AudioWebSocketOptions settings,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[Math.Min(settings.MaxMessageBytes, 64 * 1024)];
        var currentMessageBytes = 0;

        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            var received = await socket.ReceiveAsync(buffer, cancellationToken);

            if (received.MessageType == WebSocketMessageType.Close)
                return;

            if (received.MessageType != WebSocketMessageType.Binary)
            {
                await CloseOutputSafelyAsync(
                    socket,
                    WebSocketCloseStatus.InvalidMessageType,
                    "binary audio messages are required",
                    cancellationToken);
                return;
            }

            currentMessageBytes = checked(currentMessageBytes + received.Count);

            if (currentMessageBytes > settings.MaxMessageBytes)
            {
                await CloseOutputSafelyAsync(
                    socket,
                    WebSocketCloseStatus.MessageTooBig,
                    "audio message too large",
                    cancellationToken);
                return;
            }

            if (received.Count > 0)
            {
                await session.AppendAudioAsync(
                    buffer.AsMemory(0, received.Count),
                    cancellationToken);
            }

            if (received.EndOfMessage)
                currentMessageBytes = 0;
        }
    }

    private static async Task PumpTranscriptsAsync(
        WebSocket socket,
        ISpeechToTextSession session,
        CancellationToken cancellationToken)
    {
        await foreach (var update in session.ReadUpdatesAsync(cancellationToken))
        {
            if (update.Status is SpeechTranscriptStatus.Failed or SpeechTranscriptStatus.Cancelled)
            {
                var (code, message) = MapSpeechError(update);
                await SendAsync(socket, new
                {
                    type = "error",
                    code,
                    message
                }, cancellationToken);

                await CloseOutputSafelyAsync(
                    socket,
                    WebSocketCloseStatus.PolicyViolation,
                    code,
                    cancellationToken);
                return;
            }

            if (string.IsNullOrWhiteSpace(update.Text))
                continue;

            await SendAsync(socket, new
            {
                type = update.IsFinal ? "final_transcript" : "partial_transcript",
                text = update.Text,
                confidence = update.Confidence
            }, cancellationToken);
        }
    }

    private static (string Code, string Message) MapSpeechError(
        SpeechTranscriptUpdate update)
    {
        if (update.Status == SpeechTranscriptStatus.Cancelled)
        {
            return (
                "speech_cancelled",
                "Speech recognition was cancelled. Start the microphone again when ready.");
        }

        return update.Error?.Code switch
        {
            "provider_unavailable" => (
                "speech_provider_unavailable",
                "Speech recognition is temporarily unavailable. Reconnect and try again."),
            "invalid_provider_response" => (
                "speech_invalid_provider_response",
                "Speech recognition returned an invalid response. Try again."),
            _ => (
                "speech_transcription_failed",
                "Speech could not be transcribed. Start the microphone again.")
        };
    }

    private static async Task CloseOutputSafelyAsync(
        WebSocket socket,
        WebSocketCloseStatus status,
        string description,
        CancellationToken cancellationToken)
    {
        if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            await socket.CloseOutputAsync(status, description, cancellationToken);
        }
    }

    private static async Task SendAsync(
        WebSocket socket,
        object payload,
        CancellationToken cancellationToken)
    {
        if (socket.State != WebSocketState.Open)
            return;

        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
        await socket.SendAsync(
            bytes,
            WebSocketMessageType.Text,
            endOfMessage: true,
            cancellationToken);
    }
}
