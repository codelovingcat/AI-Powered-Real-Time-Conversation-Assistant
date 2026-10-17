using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Channels;
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
using Xunit.Abstractions;

namespace Conversa.Api.Tests.Integration;

/// <summary>
/// Repeatable CI load probes using synthetic users, providers, conversations, and persistence.
/// These are concurrency/leak regression tests, not production-capacity benchmarks.
/// </summary>
public sealed class RealtimeConversationLoadTests(ITestOutputHelper output)
{
    private static readonly TimeSpan ScenarioThreshold = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ProviderBarrierTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Concurrent_assistant_requests_complete_and_persist_once()
    {
        const int requestCount = 32;
        var userId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
        var conversations = Enumerable.Range(0, requestCount)
            .Select(index => Conversation.Create(
                userId,
                $"Synthetic load {index}",
                "Translate the synthetic speaker input into Turkish.",
                "en",
                "tr",
                DateTimeOffset.UtcNow))
            .ToArray();

        var conversationRepository = new ConcurrentConversationRepository(conversations);
        var messageRepository = new ConcurrentMessageRepository();
        var provider = new ConcurrentSyntheticAiProvider(requestCount);
        var unitOfWork = new CountingUnitOfWork();
        var assistant = new ConversationAssistant(
            new TestCurrentUser(userId),
            conversationRepository,
            messageRepository,
            provider,
            unitOfWork,
            TimeProvider.System);

        using var cancellation = new CancellationTokenSource(ScenarioThreshold);
        var latencies = new ConcurrentBag<double>();
        var stopwatch = Stopwatch.StartNew();

        var tasks = conversations.Select(async (conversation, index) =>
        {
            var requestWatch = Stopwatch.StartNew();
            var result = await assistant.ProcessAsync(
                new ProcessConversationInputCommand(
                    conversation.Id,
                    AiInputKind.HeardSpeech,
                    $"Synthetic input {index}"),
                cancellation.Token);
            requestWatch.Stop();
            latencies.Add(requestWatch.Elapsed.TotalMilliseconds);
            return result;
        }).ToArray();

        var results = await Task.WhenAll(tasks).WaitAsync(ScenarioThreshold, cancellation.Token);
        stopwatch.Stop();

        Assert.Equal(requestCount, results.Length);
        Assert.Equal(requestCount, results.Select(result => result.ConversationId).Distinct().Count());
        Assert.All(results, result => Assert.StartsWith("Synthetic translation:", result.Translation));
        Assert.Equal(requestCount, provider.RequestCount);
        Assert.Equal(requestCount, messageRepository.Count);
        Assert.Equal(requestCount, unitOfWork.SaveCount);
        Assert.Equal(requestCount, provider.MaximumConcurrentCalls);
        Assert.Equal(0, provider.ActiveCalls);

        var values = latencies.ToArray();
        var p50 = Percentile(values, 0.50);
        var p95 = Percentile(values, 0.95);

        output.WriteLine(
            "scenario=assistant-concurrency requests={0} total_ms={1:F1} p50_ms={2:F1} p95_ms={3:F1} max_provider_in_flight={4} persisted={5} saves={6} active_provider_calls={7} errors=0",
            requestCount,
            stopwatch.Elapsed.TotalMilliseconds,
            p50,
            p95,
            provider.MaximumConcurrentCalls,
            messageRepository.Count,
            unitOfWork.SaveCount,
            provider.ActiveCalls);

        Assert.True(
            stopwatch.Elapsed < ScenarioThreshold,
            $"The {requestCount}-request synthetic assistant load exceeded {ScenarioThreshold.TotalSeconds:F0}s.");
    }

    [Fact]
    public async Task Concurrent_authenticated_audio_websockets_emit_transcripts_and_release_sessions()
    {
        const int sessionCount = 16;
        var userId = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
        var conversations = Enumerable.Range(0, sessionCount)
            .Select(index => Conversation.Create(
                userId,
                $"Synthetic audio {index}",
                "Translate synthetic audio into Turkish.",
                "en",
                "tr",
                DateTimeOffset.UtcNow))
            .ToArray();
        foreach (var conversation in conversations)
        {
            conversation.Start(DateTimeOffset.UtcNow);
        }

        var conversationRepository = new ConcurrentConversationRepository(conversations);
        var speechFactory = new ConcurrentSyntheticSpeechSessionFactory(sessionCount);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing"
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting();
        builder.Services.AddSingleton<IConversationRepository>(conversationRepository);
        builder.Services.AddSingleton<ISpeechToTextSessionFactory>(speechFactory);
        builder.Services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = LoadTestAuthenticationHandler.SchemeName;
                options.DefaultChallengeScheme = LoadTestAuthenticationHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, LoadTestAuthenticationHandler>(
                LoadTestAuthenticationHandler.SchemeName,
                _ => { });
        builder.Services.AddAuthorization();

        await using var app = builder.Build();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseWebSockets();
        app.MapAudioWebSocket();
        await app.StartAsync();

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var latencies = new ConcurrentBag<double>();
        var stopwatch = Stopwatch.StartNew();

        var tasks = conversations.Select(async conversation =>
        {
            var connectionWatch = Stopwatch.StartNew();
            var client = app.GetTestServer().CreateWebSocketClient();
            using var socket = await client.ConnectAsync(
                new Uri($"ws://localhost/ws/conversations/{conversation.Id}/audio"),
                cancellation.Token);

            // Synchronize all peers after they have authenticated and opened their synthetic STT session.
            await speechFactory.AllSessionsOpened.WaitAsync(ProviderBarrierTimeout, cancellation.Token);

            await socket.SendAsync(
                new byte[32],
                WebSocketMessageType.Binary,
                endOfMessage: true,
                cancellation.Token);

            var partial = await ReceiveTranscriptAsync(socket, cancellation.Token);
            var final = await ReceiveTranscriptAsync(socket, cancellation.Token);

            Assert.Equal("partial_transcript", partial.Type);
            Assert.Equal($"partial:{conversation.Id:N}", partial.Text);
            Assert.Equal("final_transcript", final.Type);
            Assert.Equal($"final:{conversation.Id:N}", final.Text);

            await socket.CloseOutputAsync(
                WebSocketCloseStatus.NormalClosure,
                "synthetic load complete",
                cancellation.Token);

            connectionWatch.Stop();
            latencies.Add(connectionWatch.Elapsed.TotalMilliseconds);
        }).ToArray();

        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(20), cancellation.Token);
        await speechFactory.AllSessionsDisposed.WaitAsync(
            TimeSpan.FromSeconds(5),
            cancellation.Token);
        stopwatch.Stop();

        Assert.Equal(sessionCount, speechFactory.OpenedSessionCount);
        Assert.Equal(sessionCount, speechFactory.DisposeAttemptCount);
        Assert.Equal(sessionCount, speechFactory.DisposedSessionCount);
        Assert.Equal(0, speechFactory.ActiveSessionCount);
        Assert.Equal(sessionCount, speechFactory.ReceivedAudioChunkCount);
        Assert.Equal(sessionCount, speechFactory.MaximumConcurrentSessions);

        var values = latencies.ToArray();
        var p50 = Percentile(values, 0.50);
        var p95 = Percentile(values, 0.95);

        output.WriteLine(
            "scenario=authenticated-websocket-load sessions={0} total_ms={1:F1} p50_ms={2:F1} p95_ms={3:F1} max_active_sessions={4} opened={5} disposed={6} dispose_calls={7} audio_chunks={8} active_after={9} errors=0",
            sessionCount,
            stopwatch.Elapsed.TotalMilliseconds,
            p50,
            p95,
            speechFactory.MaximumConcurrentSessions,
            speechFactory.OpenedSessionCount,
            speechFactory.DisposedSessionCount,
            speechFactory.DisposeAttemptCount,
            speechFactory.ReceivedAudioChunkCount,
            speechFactory.ActiveSessionCount);

        Assert.True(
            stopwatch.Elapsed < ScenarioThreshold,
            $"The {sessionCount}-socket synthetic load exceeded {ScenarioThreshold.TotalSeconds:F0}s.");
    }

    private static async Task<TranscriptPayload> ReceiveTranscriptAsync(
        WebSocket socket,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        using var message = new MemoryStream();
        ValueWebSocketReceiveResult received;

        do
        {
            received = await socket.ReceiveAsync(buffer.AsMemory(), cancellationToken);
            Assert.Equal(WebSocketMessageType.Text, received.MessageType);
            await message.WriteAsync(buffer.AsMemory(0, received.Count), cancellationToken);
        }
        while (!received.EndOfMessage);

        return JsonSerializer.Deserialize<TranscriptPayload>(
            message.ToArray(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    private static double Percentile(IEnumerable<double> samples, double percentile)
    {
        var ordered = samples.Order().ToArray();
        if (ordered.Length == 0)
        {
            throw new InvalidOperationException("A load run must record at least one latency sample.");
        }

        var index = Math.Clamp((int)Math.Ceiling(percentile * ordered.Length) - 1, 0, ordered.Length - 1);
        return ordered[index];
    }

    private sealed record TranscriptPayload(string Type, string Text);

    private sealed class TestCurrentUser(Guid userId) : ICurrentUser
    {
        public Guid UserId => userId;
    }

    private sealed class ConcurrentConversationRepository : IConversationRepository
    {
        private readonly ConcurrentDictionary<Guid, Conversation> _conversations;

        public ConcurrentConversationRepository(IEnumerable<Conversation> conversations)
        {
            _conversations = new ConcurrentDictionary<Guid, Conversation>(
                conversations.ToDictionary(conversation => conversation.Id));
        }

        public Task<Conversation?> GetByIdAsync(
            Guid id,
            Guid userId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<Conversation?>(
                _conversations.TryGetValue(id, out var conversation) && conversation.UserId == userId
                    ? conversation
                    : null);
        }

        public Task<IReadOnlyList<Conversation>> ListByUserAsync(
            Guid userId,
            int take,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<Conversation> result = _conversations.Values
                .Where(conversation => conversation.UserId == userId)
                .OrderByDescending(conversation => conversation.UpdatedAt)
                .Take(take)
                .ToArray();
            return Task.FromResult(result);
        }

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

        public Task AddAsync(Conversation conversation, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_conversations.TryAdd(conversation.Id, conversation))
            {
                throw new InvalidOperationException("Duplicate synthetic conversation id.");
            }

            return Task.CompletedTask;
        }

        public void Remove(Conversation conversation)
            => _conversations.TryRemove(conversation.Id, out _);
    }

    private sealed class ConcurrentMessageRepository : IMessageRepository
    {
        private readonly ConcurrentDictionary<Guid, ConcurrentQueue<Message>> _byConversation = new();
        private readonly ConcurrentQueue<Message> _added = new();

        public int Count => _added.Count;

        public Task<IReadOnlyList<Message>> ListByConversationAsync(
            Guid conversationId,
            int take,
            CancellationToken cancellationToken)
            => ListRecentAsync(conversationId, take, cancellationToken);

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
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<Message> result = _byConversation.TryGetValue(conversationId, out var queue)
                ? queue.ToArray()
                    .OrderByDescending(message => message.CreatedAt)
                    .Take(take)
                    .Reverse()
                    .ToArray()
                : Array.Empty<Message>();
            return Task.FromResult(result);
        }

        public Task AddAsync(Message message, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _byConversation.GetOrAdd(message.ConversationId, _ => new ConcurrentQueue<Message>())
                .Enqueue(message);
            _added.Enqueue(message);
            return Task.CompletedTask;
        }
    }

    private sealed class CountingUnitOfWork : IUnitOfWork
    {
        private int _saveCount;

        public int SaveCount => Volatile.Read(ref _saveCount);

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _saveCount);
            return Task.CompletedTask;
        }
    }

    private sealed class ConcurrentSyntheticAiProvider(int expectedRequests) : IAiProvider
    {
        private readonly TaskCompletionSource<bool> _allRequestsStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _requestCount;
        private int _activeCalls;
        private int _maximumConcurrentCalls;

        public string Name => "synthetic-load";
        public int RequestCount => Volatile.Read(ref _requestCount);
        public int ActiveCalls => Volatile.Read(ref _activeCalls);
        public int MaximumConcurrentCalls => Volatile.Read(ref _maximumConcurrentCalls);

        public async Task<AiAssistantResponse> ProcessAsync(
            AiConversationRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _requestCount);
            var current = Interlocked.Increment(ref _activeCalls);
            UpdateMaximum(ref _maximumConcurrentCalls, current);

            if (current == expectedRequests)
            {
                _allRequestsStarted.TrySetResult(true);
            }

            try
            {
                await _allRequestsStarted.Task.WaitAsync(ProviderBarrierTimeout, cancellationToken);
                await Task.Delay(TimeSpan.FromMilliseconds(15), cancellationToken);
                return new AiAssistantResponse(
                    AiResponseType.Translation,
                    request.InputText,
                    $"Synthetic translation: {request.InputText}",
                    null,
                    null,
                    null,
                    QuestionDetected: false,
                    QuestionDirectedAtUser: false);
            }
            finally
            {
                Interlocked.Decrement(ref _activeCalls);
            }
        }
    }

    private sealed class ConcurrentSyntheticSpeechSessionFactory(int expectedSessions)
        : ISpeechToTextSessionFactory
    {
        private readonly ConcurrentDictionary<Guid, ConcurrentSyntheticSpeechSession> _sessions = new();
        private readonly TaskCompletionSource<bool> _allSessionsOpened =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _allSessionsDisposed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _openedSessionCount;
        private int _activeSessionCount;
        private int _maximumConcurrentSessions;
        private int _disposeAttemptCount;
        private int _disposedSessionCount;
        private int _receivedAudioChunkCount;

        public string ProviderName => "synthetic-load";
        public Task AllSessionsOpened => _allSessionsOpened.Task;
        public Task AllSessionsDisposed => _allSessionsDisposed.Task;
        public int OpenedSessionCount => Volatile.Read(ref _openedSessionCount);
        public int ActiveSessionCount => Volatile.Read(ref _activeSessionCount);
        public int MaximumConcurrentSessions => Volatile.Read(ref _maximumConcurrentSessions);
        public int DisposeAttemptCount => Volatile.Read(ref _disposeAttemptCount);
        public int DisposedSessionCount => Volatile.Read(ref _disposedSessionCount);
        public int ReceivedAudioChunkCount => Volatile.Read(ref _receivedAudioChunkCount);

        public Task<ISpeechToTextSession> OpenSessionAsync(
            SpeechSessionOptions options,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var session = new ConcurrentSyntheticSpeechSession(
                options.ConversationId,
                RecordAudioChunk,
                RecordDisposeAttempt,
                RecordDisposed);

            if (!_sessions.TryAdd(options.ConversationId, session))
            {
                throw new InvalidOperationException("A synthetic audio session was opened more than once.");
            }

            Interlocked.Increment(ref _openedSessionCount);
            var current = Interlocked.Increment(ref _activeSessionCount);
            UpdateMaximum(ref _maximumConcurrentSessions, current);

            if (OpenedSessionCount == expectedSessions)
            {
                _allSessionsOpened.TrySetResult(true);
            }

            return Task.FromResult<ISpeechToTextSession>(session);
        }

        private void RecordAudioChunk()
            => Interlocked.Increment(ref _receivedAudioChunkCount);

        private void RecordDisposeAttempt()
            => Interlocked.Increment(ref _disposeAttemptCount);

        private void RecordDisposed()
        {
            Interlocked.Decrement(ref _activeSessionCount);
            if (Interlocked.Increment(ref _disposedSessionCount) == expectedSessions)
            {
                _allSessionsDisposed.TrySetResult(true);
            }
        }
    }

    private sealed class ConcurrentSyntheticSpeechSession(
        Guid conversationId,
        Action recordAudioChunk,
        Action recordDisposeAttempt,
        Action recordDisposed) : ISpeechToTextSession
    {
        private readonly Channel<SpeechTranscriptUpdate> _updates =
            Channel.CreateUnbounded<SpeechTranscriptUpdate>();
        private int _hasEmitted;
        private int _disposed;

        public async ValueTask AppendAudioAsync(
            ReadOnlyMemory<byte> audioChunk,
            CancellationToken cancellationToken)
        {
            if (audioChunk.IsEmpty || Interlocked.Exchange(ref _hasEmitted, 1) != 0)
            {
                return;
            }

            recordAudioChunk();
            await _updates.Writer.WriteAsync(
                new SpeechTranscriptUpdate($"partial:{conversationId:N}", false, 0.95f),
                cancellationToken);
            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
            await _updates.Writer.WriteAsync(
                new SpeechTranscriptUpdate($"final:{conversationId:N}", true, 0.99f),
                cancellationToken);
        }

        public async IAsyncEnumerable<SpeechTranscriptUpdate> ReadUpdatesAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken)
        {
            await foreach (var update in _updates.Reader.ReadAllAsync(cancellationToken))
            {
                yield return update;
            }
        }

        public ValueTask DisposeAsync()
        {
            recordDisposeAttempt();
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _updates.Writer.TryComplete();
                recordDisposed();
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class LoadTestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "RealtimeLoadTest";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
                [new Claim(
                    ClaimTypes.NameIdentifier,
                    "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb")],
                SchemeName);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }

    private static void UpdateMaximum(ref int target, int candidate)
    {
        var current = Volatile.Read(ref target);
        while (candidate > current)
        {
            var observed = Interlocked.CompareExchange(ref target, candidate, current);
            if (observed == current)
            {
                return;
            }

            current = observed;
        }
    }
}
