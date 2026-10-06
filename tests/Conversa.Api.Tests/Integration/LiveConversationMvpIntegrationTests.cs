using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;
using Conversa.Api.Realtime;
using Conversa.Application.Abstractions.Identity;
using Conversa.Application.Abstractions.Persistence;
using Conversa.Application.Ai;
using Conversa.Application.Conversations;
using Conversa.Application.Speech;
using Conversa.Domain.Conversations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Conversa.Api.Tests.Integration;

public sealed class LiveConversationMvpIntegrationTests
{
    [Fact]
    public async Task Live_conversation_flow_transcript_to_assistant_result_and_history()
    {
        var userId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var conversation = Conversation.Create(
            userId,
            "Live MVP",
            "Translate English naturally into Turkish, explain the meaning, and suggest a reply when the speaker asks me a question.",
            "en",
            "tr",
            DateTimeOffset.UtcNow);

        var messages = new RecordingMessageRepository();
        var aiProvider = new RecordingAiProvider(
            new AiAssistantResponse(
                AiResponseType.Question,
                "Would you like some coffee?",
                "Kahve ister misin?",
                "The speaker is asking whether you want coffee.",
                "Yes, please. That would be great.",
                "Evet, lütfen. Harika olur.",
                QuestionDetected: true,
                QuestionDirectedAtUser: true));

        var speechSession = new RecordingSpeechSession();
        var builder = new WebApplicationOptions { EnvironmentName = "Testing" };
        var appBuilder = WebApplication.CreateBuilder(builder);
        appBuilder.WebHost.UseTestServer();

        appBuilder.Services.AddRouting();
        appBuilder.Services.Configure<AudioWebSocketOptions>(_ => { });
        appBuilder.Services.AddSingleton<IConversationRepository>(
            new RecordingConversationRepository(conversation));
        appBuilder.Services.AddSingleton<IMessageRepository>(messages);
        appBuilder.Services.AddSingleton<IAiProvider>(aiProvider);
        appBuilder.Services.AddSingleton<ISpeechToTextSessionFactory>(
            new RecordingSpeechSessionFactory(speechSession));

        appBuilder.Services.AddScoped<IConversationAssistant>(service =>
            new ConversationAssistant(
                new TestCurrentUser(userId),
                service.GetRequiredService<IConversationRepository>(),
                service.GetRequiredService<IMessageRepository>(),
                service.GetRequiredService<IAiProvider>(),
                new RecordingUnitOfWork(),
                TimeProvider.System));

        appBuilder.Services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthenticationHandler.TestScheme;
                options.DefaultChallengeScheme = TestAuthenticationHandler.TestScheme;
            })
            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                TestAuthenticationHandler.TestScheme,
                _ => { });

        appBuilder.Services.AddAuthorization();

        await using var app = appBuilder.Build();

        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseWebSockets();
        app.MapAudioWebSocket();

        await app.StartAsync();

        using var socket = await app.GetTestServer()
            .CreateWebSocketClient()
            .ConnectAsync(
                new Uri($"ws://localhost/ws/conversations/{conversation.Id}/audio"),
                CancellationToken.None);

        await socket.SendAsync(
            Encoding.UTF8.GetBytes("test-audio"),
            WebSocketMessageType.Binary,
            true,
            CancellationToken.None);

        var partial = await ReceiveTranscriptAsync(socket);
        var final = await ReceiveTranscriptAsync(socket);

        Assert.Equal("partial_transcript", partial.Type);
        Assert.Equal("Would you like some coffee", partial.Text);
        Assert.Equal("final_transcript", final.Type);
        Assert.Equal("Would you like some coffee?", final.Text);
        Assert.Equal(1, speechSession.ReceivedChunkCount);

        await using var scope = app.Services.CreateAsyncScope();
        var assistant = scope.ServiceProvider.GetRequiredService<IConversationAssistant>();
        var result = await assistant.ProcessAsync(
            new ProcessConversationInputCommand(
                conversation.Id,
                AiInputKind.HeardSpeech,
                final.Text),
            CancellationToken.None);

        Assert.Equal("Would you like some coffee?", result.OriginalText);
        Assert.Equal("Kahve ister misin?", result.Translation);
        Assert.Equal("Yes, please. That would be great.", result.SuggestedAnswer);
        Assert.Equal("Evet, lütfen. Harika olur.", result.SuggestedAnswerTranslation);
        Assert.True(result.QuestionDetected);
        Assert.True(result.QuestionDirectedAtUser);
        Assert.Equal("gemini", result.ProviderName);

        var persisted = Assert.Single(messages.AddedMessages);
        Assert.Equal(result.Id, persisted.Id);
        Assert.Equal(conversation.Id, persisted.ConversationId);
        Assert.Equal(MessageRole.Speaker, persisted.Role);

        var history = await messages.ListByConversationAsync(
            conversation.Id,
            20,
            CancellationToken.None);

        var historyMessage = Assert.Single(history);
        Assert.Equal("Would you like some coffee?", historyMessage.OriginalText);
        Assert.Equal("Kahve ister misin?", historyMessage.Translation);
        Assert.Equal("Yes, please. That would be great.", historyMessage.SuggestedAnswer);

        Assert.Equal(
            conversation.Instruction,
            Assert.Single(aiProvider.Requests).Instruction);

        socket.Abort();
        await speechSession.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await app.StopAsync();
    }

    private static async Task<TranscriptPayload> ReceiveTranscriptAsync(WebSocket socket)
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

        return JsonSerializer.Deserialize<TranscriptPayload>(
            message.ToArray(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    private sealed record TranscriptPayload(string Type, string Text);

    private sealed class TestCurrentUser(Guid userId) : ICurrentUser
    {
        public Guid UserId => userId;
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
            => Task.FromResult<IReadOnlyList<Conversation>>([conversation]);

        public Task<IReadOnlyList<Conversation>> ListByUserPageAsync(
            Guid userId,
            int take,
            DateTimeOffset? beforeUpdatedAt,
            Guid? beforeId,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task AddAsync(Conversation conversation, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public void Remove(Conversation conversation)
            => throw new NotSupportedException();
    }

    private sealed class RecordingMessageRepository : IMessageRepository
    {
        private readonly List<Message> _messages = [];

        public List<Message> AddedMessages { get; } = [];

        public Task<IReadOnlyList<Message>> ListByConversationAsync(
            Guid conversationId,
            int take,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<Message>>(
                _messages
                    .Where(message => message.ConversationId == conversationId)
                    .OrderBy(message => message.CreatedAt)
                    .Take(take)
                    .ToArray());

        public Task<IReadOnlyList<Message>> ListByConversationPageAsync(
            Guid conversationId,
            int take,
            DateTimeOffset? beforeCreatedAt,
            Guid? beforeId,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<Message>> ListRecentAsync(
            Guid conversationId,
            int take,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<Message>>(
                _messages
                    .Where(message => message.ConversationId == conversationId)
                    .OrderByDescending(message => message.CreatedAt)
                    .Take(take)
                    .Reverse()
                    .ToArray());

        public Task AddAsync(Message message, CancellationToken cancellationToken)
        {
            _messages.Add(message);
            AddedMessages.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingUnitOfWork : IUnitOfWork
    {
        public Task SaveChangesAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class RecordingAiProvider(AiAssistantResponse response) : IAiProvider
    {
        public string Name => "gemini";

        public List<AiConversationRequest> Requests { get; } = [];

        public Task<AiAssistantResponse> ProcessAsync(
            AiConversationRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(response);
        }
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
                "Would you like some coffee",
                false,
                0.96f);

            yield return new SpeechTranscriptUpdate(
                "Would you like some coffee?",
                true,
                0.99f);
        }

        public ValueTask DisposeAsync()
        {
            Disposed.TrySetResult(null);
            return ValueTask.CompletedTask;
        }
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
                    "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")],
                TestScheme);

            return Task.FromResult(
                AuthenticateResult.Success(
                    new AuthenticationTicket(
                        new ClaimsPrincipal(identity),
                        TestScheme)));
        }
    }
}
