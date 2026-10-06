using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Threading.RateLimiting;
using Conversa.Api.Realtime;
using Conversa.Application.Abstractions.Persistence;
using Conversa.Application.Speech;
using Conversa.Domain.Conversations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Conversa.Api.Tests.Integration;

public sealed class DeepgramWebSocketIntegrationTests
{
    [Fact]
    public async Task Authenticated_audio_websocket_forwards_binary_audio_and_returns_transcript_updates()
    {
        var userId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var conversation = Conversation.Create(
            userId,
            "WebSocket integration test",
            "Translate English into natural Turkish.",
            "en",
            "tr",
            DateTimeOffset.UtcNow);

        var session = new RecordingSpeechSession();
        var sessionOptions = new List<SpeechSessionOptions>();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddRouting();
        builder.Services.Configure<AudioWebSocketOptions>(_ => { });
        builder.Services.AddRateLimiter(options =>
        {
            options.AddPolicy(
                "audio",
                context => RateLimitPartition.GetNoLimiter(
                    context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "test"));
        });

        builder.Services.AddSingleton<IConversationRepository>(
            new RecordingConversationRepository(conversation));

        builder.Services.AddSingleton<ISpeechToTextSessionFactory>(
            new RecordingSpeechSessionFactory(session, sessionOptions));

        builder.Services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthenticationHandler.TestScheme;
                options.DefaultChallengeScheme = TestAuthenticationHandler.TestScheme;
            })
            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                TestAuthenticationHandler.TestScheme,
                _ => { });

        builder.Services.AddAuthorization();

        await using var app = builder.Build();

        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        app.UseWebSockets();
        app.MapAudioWebSocket();

        await app.StartAsync();

        var client = app.GetTestServer().CreateWebSocketClient();
        using var socket = await client.ConnectAsync(
            new Uri(
                $"ws://localhost/ws/conversations/{conversation.Id}/audio"),
            CancellationToken.None);

        await socket.SendAsync(
            new byte[] { 0x01, 0x02, 0x03, 0x04 },
            WebSocketMessageType.Binary,
            endOfMessage: true,
            CancellationToken.None);

        var first = await ReceiveTextAsync(socket);
        var second = await ReceiveTextAsync(socket);

        using var firstPayload = JsonDocument.Parse(first);
        using var secondPayload = JsonDocument.Parse(second);

        Assert.Equal(
            "partial_transcript",
            firstPayload.RootElement.GetProperty("type").GetString());
        Assert.Equal(
            "Hello",
            firstPayload.RootElement.GetProperty("text").GetString());
        Assert.Equal(
            "final_transcript",
            secondPayload.RootElement.GetProperty("type").GetString());
        Assert.Equal(
            "Hello, world.",
            secondPayload.RootElement.GetProperty("text").GetString());
        Assert.Equal(1, session.ReceivedChunkCount);
        Assert.Single(sessionOptions);
        Assert.Equal(conversation.Id, sessionOptions[0].ConversationId);
        Assert.Equal("en", sessionOptions[0].Language);
        Assert.Equal("audio/raw", sessionOptions[0].ContentType);
        Assert.Equal(16000, sessionOptions[0].SampleRateHertz);

        socket.Abort();

        await session.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await app.StopAsync();
    }

    private static async Task<string> ReceiveTextAsync(WebSocket socket)
    {
        var buffer = new byte[4096];
        using var message = new MemoryStream();

        WebSocketReceiveResult received;
        do
        {
            received = await socket.ReceiveAsync(buffer, CancellationToken.None);
            Assert.Equal(WebSocketMessageType.Text, received.MessageType);

            await message.WriteAsync(buffer.AsMemory(0, received.Count));
        }
        while (!received.EndOfMessage);

        return Encoding.UTF8.GetString(message.ToArray());
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string TestScheme = "Test";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
                [new Claim(
                    ClaimTypes.NameIdentifier,
                    "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")],
                TestScheme);

            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, TestScheme);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    private sealed class RecordingConversationRepository(Conversation conversation)
        : IConversationRepository
    {
        public Task<Conversation?> GetByIdAsync(
            Guid id,
            Guid userId,
            CancellationToken cancellationToken)
            => Task.FromResult<Conversation?>(
                id == conversation.Id && userId == conversation.UserId
                    ? conversation
                    : null);

        public Task<IReadOnlyList<Conversation>> ListByUserAsync(
            Guid userId,
            int take,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<Conversation>> ListByUserPageAsync(
            Guid userId,
            int take,
            string? search,
            DateTimeOffset? updatedFrom,
            DateTimeOffset? updatedTo,
            DateTimeOffset? beforeUpdatedAt,
            Guid? beforeId,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task AddAsync(
            Conversation conversation,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public void Remove(Conversation conversation)
            => throw new NotSupportedException();
    }

    private sealed class RecordingSpeechSessionFactory(
        RecordingSpeechSession session,
        List<SpeechSessionOptions> sessionOptions)
        : ISpeechToTextSessionFactory
    {
        public string ProviderName => "test";

        public Task<ISpeechToTextSession> OpenSessionAsync(
            SpeechSessionOptions options,
            CancellationToken cancellationToken)
        {
            sessionOptions.Add(options);
            return Task.FromResult<ISpeechToTextSession>(session);
        }
    }

    private sealed class RecordingSpeechSession : ISpeechToTextSession
    {
        public int ReceivedChunkCount { get; private set; }

        public TaskCompletionSource<object?> Disposed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask AppendAudioAsync(
            ReadOnlyMemory<byte> audioChunk,
            CancellationToken cancellationToken)
        {
            ReceivedChunkCount++;
            return ValueTask.CompletedTask;
        }

        public async IAsyncEnumerable<SpeechTranscriptUpdate> ReadUpdatesAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken)
        {
            await Task.Yield();

            yield return new SpeechTranscriptUpdate(
                "Hello",
                false,
                0.91f);

            yield return new SpeechTranscriptUpdate(
                "Hello, world.",
                true,
                0.98f);
        }

        public ValueTask DisposeAsync()
        {
            Disposed.TrySetResult(null);
            return ValueTask.CompletedTask;
        }
    }
}
