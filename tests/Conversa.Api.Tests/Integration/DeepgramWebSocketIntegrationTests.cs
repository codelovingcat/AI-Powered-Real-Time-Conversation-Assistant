using System.Net.WebSockets;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text;
using Conversa.Application.Abstractions.Persistence;
using Conversa.Application.Speech;
using Conversa.Api.Realtime;
using Conversa.Domain.Conversations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
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
        using var factory = new TestAppFactory();

        var session = factory.Session;

        var client = factory.Server.CreateWebSocketClient();
        using var socket = await client.ConnectAsync(
            new Uri(
                $"ws://localhost/ws/conversations/{factory.Conversation.Id}/audio"),
            CancellationToken.None);

        await socket.SendAsync(
            new byte[] { 0x01, 0x02, 0x03, 0x04 },
            WebSocketMessageType.Binary,
            endOfMessage: true,
            CancellationToken.None);

        var first = await ReceiveTextAsync(socket);
        var second = await ReceiveTextAsync(socket);

        Assert.Contains(""type":"partial_transcript"", first);
        Assert.Contains(""text":"Hello"", first);
        Assert.Contains(""type":"final_transcript"", second);
        Assert.Contains(""text":"Hello, world."", second);
        Assert.Equal(1, session.ReceivedChunkCount);

        await socket.CloseAsync(
            WebSocketCloseStatus.NormalClosure,
            "test complete",
            CancellationToken.None);

        await session.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
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

    private sealed class TestAppFactory : WebApplicationFactory<Program>
    {
        private static readonly Guid TestUserId =
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        public TestAppFactory()
        {
            Conversation = Conversation.Create(
                TestUserId,
                "WebSocket integration test",
                "Translate English into natural Turkish.",
                "en",
                "tr",
                DateTimeOffset.UtcNow);

            Session = new RecordingSpeechSession();

            ServerRepository = new RecordingConversationRepository(Conversation);
            SessionFactory = new RecordingSpeechSessionFactory(Session);
        }

        public Conversation Conversation { get; }

        public RecordingSpeechSession Session { get; }

        private RecordingConversationRepository ServerRepository { get; }

        private RecordingSpeechSessionFactory SessionFactory { get; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Test");

            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(
                [
                    new KeyValuePair<string, string?>(
                        "ConnectionStrings:DefaultConnection",
                        "Host=localhost;Port=5432;Database=conversa;Username=conversa;Password=test"),
                    new KeyValuePair<string, string?>("Gemini:ApiKey", "test-key"),
                    new KeyValuePair<string, string?>("Authentication:Issuer", "test"),
                    new KeyValuePair<string, string?>("Authentication:Audience", "test"),
                    new KeyValuePair<string, string?>(
                        "Authentication:SigningKey",
                        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")
                ]);
            });

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IConversationRepository>();
                services.AddSingleton<IConversationRepository>(ServerRepository);

                services.RemoveAll<ISpeechToTextSessionFactory>();
                services.AddSingleton<ISpeechToTextSessionFactory>(SessionFactory);

                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthenticationHandler.Scheme;
                    options.DefaultChallengeScheme = TestAuthenticationHandler.Scheme;
                }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                    TestAuthenticationHandler.Scheme,
                    _ => { });
            });
        }
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string Scheme = "Test";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, TestUserIdValue())],
                Scheme);

            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }

        private static string TestUserIdValue()
            => "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
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

        public Task AddAsync(
            Conversation conversation,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public void Remove(Conversation conversation)
            => throw new NotSupportedException();
    }

    private sealed class RecordingSpeechSessionFactory(RecordingSpeechSession session)
        : ISpeechToTextSessionFactory
    {
        public string ProviderName => "test";

        public Task<ISpeechToTextSession> OpenSessionAsync(
            SpeechSessionOptions options,
            CancellationToken cancellationToken)
            => Task.FromResult<ISpeechToTextSession>(session);
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
