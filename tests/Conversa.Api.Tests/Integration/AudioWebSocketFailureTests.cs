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
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Conversa.Api.Tests.Integration;

public sealed class AudioWebSocketFailureTests
{
    [Fact]
    public async Task Provider_open_failure_returns_neutral_error_without_provider_details()
    {
        var userId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var conversation = CreateActiveConversation(userId);
        var app = await StartServerAsync(
            conversation,
            new FailingSpeechSessionFactory());

        try
        {
            using var socket = await ConnectAsync(app, conversation.Id);
            var response = await ReceiveTextAsync(socket);
            using var payload = JsonDocument.Parse(response);

            Assert.Equal("error", payload.RootElement.GetProperty("type").GetString());
            Assert.Equal(
                "speech_provider_unavailable",
                payload.RootElement.GetProperty("code").GetString());
            Assert.DoesNotContain(
                "sensitive provider diagnostic",
                response,
                StringComparison.OrdinalIgnoreCase);

            var close = await ReceiveCloseAsync(socket);
            Assert.Equal(WebSocketMessageType.Close, close.MessageType);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Provider_failure_after_partial_transcript_preserves_partial_and_closes_cleanly()
    {
        var userId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var conversation = CreateActiveConversation(userId);
        var session = new FailingSpeechSession();
        var app = await StartServerAsync(
            conversation,
            new FixedSpeechSessionFactory(session));

        try
        {
            using var socket = await ConnectAsync(app, conversation.Id);

            var partial = await ReceiveTextAsync(socket);
            using var partialPayload = JsonDocument.Parse(partial);
            Assert.Equal(
                "partial_transcript",
                partialPayload.RootElement.GetProperty("type").GetString());
            Assert.Equal(
                "The interview starts at nine.",
                partialPayload.RootElement.GetProperty("text").GetString());

            var failure = await ReceiveTextAsync(socket);
            using var failurePayload = JsonDocument.Parse(failure);
            Assert.Equal("error", failurePayload.RootElement.GetProperty("type").GetString());
            Assert.Equal(
                "speech_provider_unavailable",
                failurePayload.RootElement.GetProperty("code").GetString());
            Assert.DoesNotContain(
                "raw provider payload",
                failure,
                StringComparison.OrdinalIgnoreCase);

            var close = await ReceiveCloseAsync(socket);
            Assert.Equal(WebSocketMessageType.Close, close.MessageType);
            await session.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    private static Conversation CreateActiveConversation(Guid userId)
    {
        var conversation = Conversation.Create(
            userId,
            "STT failure integration test",
            "Translate English into Turkish.",
            "en",
            "tr",
            DateTimeOffset.UtcNow);
        conversation.Start(DateTimeOffset.UtcNow);
        return conversation;
    }

    private static async Task<WebApplication> StartServerAsync(
        Conversation conversation,
        ISpeechToTextSessionFactory sessionFactory)
    {
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
        builder.Services.AddSingleton(sessionFactory);
        builder.Services.AddSingleton<ISpeechToTextSessionFactory>(sessionFactory);

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

        var app = builder.Build();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        app.UseWebSockets();
        app.MapAudioWebSocket();

        await app.StartAsync();
        return app;
    }

    private static async Task<WebSocket> ConnectAsync(
        WebApplication app,
        Guid conversationId)
    {
        var client = app.GetTestServer().CreateWebSocketClient();
        return await client.ConnectAsync(
            new Uri($"ws://localhost/ws/conversations/{conversationId}/audio"),
            CancellationToken.None);
    }

    private static async Task<string> ReceiveTextAsync(WebSocket socket)
    {
        var buffer = new byte[4096];
        using var message = new MemoryStream();

        WebSocketReceiveResult received;
        do
        {
            received = await socket.ReceiveAsync(
                buffer,
                CancellationToken.None);
            Assert.Equal(WebSocketMessageType.Text, received.MessageType);
            await message.WriteAsync(buffer.AsMemory(0, received.Count));
        }
        while (!received.EndOfMessage);

        return Encoding.UTF8.GetString(message.ToArray());
    }

    private static async Task<WebSocketReceiveResult> ReceiveCloseAsync(WebSocket socket)
    {
        var buffer = new byte[1024];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        return await socket.ReceiveAsync(buffer, timeout.Token);
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

            // The second scenario uses a different conversation owner; only the
            // authenticated test identity is needed for its isolated test server.
            if (Request.Path.Value?.Contains("bbbbbbbb", StringComparison.OrdinalIgnoreCase) == true)
            {
                identity = new ClaimsIdentity(
                    [new Claim(
                        ClaimTypes.NameIdentifier,
                        "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")],
                    TestScheme);
            }

            return Task.FromResult(
                AuthenticateResult.Success(
                    new AuthenticationTicket(new ClaimsPrincipal(identity), TestScheme)));
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

    private sealed class FailingSpeechSessionFactory : ISpeechToTextSessionFactory
    {
        public string ProviderName => "test";

        public Task<ISpeechToTextSession> OpenSessionAsync(
            SpeechSessionOptions options,
            CancellationToken cancellationToken)
            => Task.FromException<ISpeechToTextSession>(
                new WebSocketException("sensitive provider diagnostic"));
    }

    private sealed class FixedSpeechSessionFactory(ISpeechToTextSession session)
        : ISpeechToTextSessionFactory
    {
        public string ProviderName => "test";

        public Task<ISpeechToTextSession> OpenSessionAsync(
            SpeechSessionOptions options,
            CancellationToken cancellationToken)
            => Task.FromResult(session);
    }

    private sealed class FailingSpeechSession : ISpeechToTextSession
    {
        public TaskCompletionSource<object?> Disposed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask AppendAudioAsync(
            ReadOnlyMemory<byte> audioChunk,
            CancellationToken cancellationToken)
            => ValueTask.CompletedTask;

        public async IAsyncEnumerable<SpeechTranscriptUpdate> ReadUpdatesAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken)
        {
            await Task.Yield();

            yield return new SpeechTranscriptUpdate(
                "The interview starts at nine.",
                false,
                0.93f);

            yield return new SpeechTranscriptUpdate(
                string.Empty,
                true,
                null,
                SpeechTranscriptStatus.Failed,
                new SpeechError(
                    "provider_unavailable",
                    "raw provider payload must not reach the client"));
        }

        public ValueTask DisposeAsync()
        {
            Disposed.TrySetResult(null);
            return ValueTask.CompletedTask;
        }
    }
}
