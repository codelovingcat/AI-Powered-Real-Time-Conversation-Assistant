using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Conversa.Api.Identity;
using Conversa.Application.Abstractions.Persistence;
using Conversa.Application.Speech;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.RateLimiting;

namespace Conversa.Api.Realtime;

public static class AudioWebSocketEndpoint
{
    private const int MaxFrameBytes = 256 * 1024;

    public static void MapAudioWebSocket(this WebApplication app)
    {
        app.Map("/ws/conversations/{conversationId:guid}/audio", HandleAsync).RequireRateLimiting("audio");
    }

    private static async Task HandleAsync(
        HttpContext context,
        Guid conversationId,
        IConversationRepository conversations,
        IServiceProvider services)
    {
        if (!HeaderCurrentUser.TryResolve(context, out var userId))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "user_required",
                message = $"Provide {HeaderCurrentUser.HeaderName} or the userId query parameter."
            });
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

        var factory = services.GetService<ISpeechToTextSessionFactory>();
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        if (factory is null)
        {
            await SendAsync(socket, new
            {
                type = "error",
                code = "stt_provider_not_configured",
                message = "No speech-to-text provider is registered. Audio was not transcribed."
            }, context.RequestAborted);
            await socket.CloseAsync(
                WebSocketCloseStatus.PolicyViolation,
                "stt_provider_not_configured",
                context.RequestAborted);
            return;
        }

        await using var session = await factory.OpenSessionAsync(
            new SpeechSessionOptions(conversation.Id, conversation.SourceLanguage, null, null),
            context.RequestAborted);

        var pumping = PumpTranscriptsAsync(socket, session, context.RequestAborted);
        await ReceiveAudioAsync(socket, session, context.RequestAborted);
        await pumping;
    }

    private static async Task ReceiveAudioAsync(
        WebSocket socket,
        ISpeechToTextSession session,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[MaxFrameBytes];
        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            var received = await socket.ReceiveAsync(buffer, cancellationToken);
            if (received.MessageType == WebSocketMessageType.Close)
            {
                break;
            }

            if (received.Count == MaxFrameBytes && !received.EndOfMessage)
            {
                await socket.CloseAsync(WebSocketCloseStatus.MessageTooBig, "frame too large", cancellationToken);
                break;
            }

            if (received.MessageType == WebSocketMessageType.Binary && received.Count > 0)
            {
                await session.AppendAudioAsync(buffer.AsMemory(0, received.Count), cancellationToken);
            }
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
