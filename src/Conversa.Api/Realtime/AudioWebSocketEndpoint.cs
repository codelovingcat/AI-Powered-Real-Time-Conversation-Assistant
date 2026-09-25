using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Security.Claims;
using Conversa.Application.Abstractions.Persistence;
using Conversa.Application.Speech;
using Microsoft.Extensions.DependencyInjection;

namespace Conversa.Api.Realtime;

public static class AudioWebSocketEndpoint
{
    private const int MaxFrameBytes = 256 * 1024;

    public static void MapAudioWebSocket(this WebApplication app)
    {
        app.Map("/ws/conversations/{conversationId:guid}/audio", HandleAsync);
    }

    private static async Task HandleAsync(
        HttpContext context,
        Guid conversationId,
        IConversationRepository conversations,
        IServiceProvider services)
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
