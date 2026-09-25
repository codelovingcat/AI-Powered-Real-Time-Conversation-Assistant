using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Conversa.Application.Abstractions.Persistence;
using Conversa.Application.Speech;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Conversa.Api.Realtime;

public static class AudioWebSocketEndpoint
{
    public static void MapAudioWebSocket(this WebApplication app)
    {
        app.Map("/ws/conversations/{conversationId:guid}/audio", HandleAsync);
    }

    private static async Task HandleAsync(
        HttpContext context,
        Guid conversationId,
        IConversationRepository conversations,
        IServiceProvider services,
        IOptions<AudioWebSocketOptions> options)
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

        var conversation = await conversations.GetByIdAsync(conversationId, userId, context.RequestAborted);
        if (conversation is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "websocket_required",
                message = "This endpoint accepts a WebSocket upgrade."
            });
            return;
        }

        var settings = options.Value;
        settings.Validate();

        using var lifetimeCts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        lifetimeCts.CancelAfter(TimeSpan.FromSeconds(settings.MaxConnectionSeconds));
        var cancellationToken = lifetimeCts.Token;

        var factory = services.GetService<ISpeechToTextSessionFactory>();
        using var socket = await context.WebSockets.AcceptWebSocketAsync();

        if (factory is null)
        {
            await SendAsync(socket, new
            {
                type = "error",
                code = "stt_provider_not_configured",
                message = "No speech-to-text provider is registered. Audio was not transcribed."
            }, cancellationToken);

            await socket.CloseAsync(
                WebSocketCloseStatus.PolicyViolation,
                "stt_provider_not_configured",
                cancellationToken);
            return;
        }

        await using var session = await factory.OpenSessionAsync(
            new SpeechSessionOptions(conversation.Id, conversation.SourceLanguage, null, null),
            cancellationToken);

        try
        {
            var pumping = PumpTranscriptsAsync(socket, session, cancellationToken);
            await ReceiveAudioAsync(socket, session, settings, cancellationToken);
            await pumping;
        }
        catch (OperationCanceledException) when (lifetimeCts.IsCancellationRequested)
        {
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                await socket.CloseAsync(
                    WebSocketCloseStatus.PolicyViolation,
                    "connection lifetime exceeded or request cancelled",
                    CancellationToken.None);
            }
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
                await socket.CloseAsync(
                    WebSocketCloseStatus.InvalidMessageType,
                    "binary audio messages are required",
                    cancellationToken);
                return;
            }

            currentMessageBytes = checked(currentMessageBytes + received.Count);

            if (currentMessageBytes > settings.MaxMessageBytes)
            {
                await socket.CloseAsync(
                    WebSocketCloseStatus.MessageTooBig,
                    "audio message too large",
                    cancellationToken);
                return;
            }

            if (received.Count > 0)
                await session.AppendAudioAsync(buffer.AsMemory(0, received.Count), cancellationToken);

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
            await SendAsync(socket, new
            {
                type = update.IsFinal ? "final_transcript" : "partial_transcript",
                text = update.Text,
                confidence = update.Confidence
            }, cancellationToken);
        }
    }

    private static async Task SendAsync(WebSocket socket, object payload, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
        await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
    }
}
