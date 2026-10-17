using System.Net;
using System.Text;
using System.Text.Json;
using Conversa.Application.Ai;
using Conversa.Application.Common;
using Conversa.Domain.Conversations;
using Conversa.Infrastructure.Ai.Gemini;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Conversa.Api.Tests.Gemini;

public sealed class GeminiConversationSummarizerTests
{
    private const string ApiKey = "TEST_SUMMARY_API_KEY_DO_NOT_LOG";
    private const string PrivateTranscript = "PRIVATE_SYNTHETIC_TRANSCRIPT_DO_NOT_LOG";

    [Fact]
    public async Task SummarizeAsync_RetriesOneTransientFailureAndReturnsValidatedSummary()
    {
        var responses = new Queue<HttpResponseMessage>(
        [
            JsonResponse(HttpStatusCode.ServiceUnavailable, "{\"error\":{\"status\":\"UNAVAILABLE\"}}"),
            JsonResponse(HttpStatusCode.OK, ProviderResponse("Compact summary of the older turns."))
        ]);
        var handler = new DelegateHandler((_, _) => Task.FromResult(responses.Dequeue()));
        using var client = new HttpClient(handler);
        var logger = new TestLogger<GeminiConversationSummarizer>();
        var summarizer = CreateSummarizer(client, logger, maxRetries: 3);
        Assert.Equal(GeminiConversationSummarizer.PromptVersionValue, summarizer.PromptVersion);

        var result = await summarizer.SummarizeAsync(
            "Keep the active instruction.",
            "en",
            "tr",
            null,
            CreateTurns(8),
            CancellationToken.None);

        Assert.Equal("Compact summary of the older turns.", result);
        Assert.Equal(2, handler.RequestCount);
        Assert.Contains(logger.Messages, message => message.Contains("503", StringComparison.Ordinal));
        Assert.DoesNotContain(PrivateTranscript, string.Join(Environment.NewLine, logger.Messages), StringComparison.Ordinal);
        Assert.DoesNotContain(ApiKey, string.Join(Environment.NewLine, logger.Messages), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SummarizeAsync_BoundsEscapedSummaryPromptAndDoesNotSendFullTranscriptFields()
    {
        string? sentUserPrompt = null;
        string? sentSystemInstruction = null;
        var handler = new DelegateHandler(async (request, _) =>
        {
            var json = await request.Content!.ReadAsStringAsync();
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            sentSystemInstruction = root
                .GetProperty("systemInstruction")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString();
            sentUserPrompt = root
                .GetProperty("contents")[0]
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString();
            return JsonResponse(HttpStatusCode.OK, ProviderResponse("Bounded summary."));
        });
        using var client = new HttpClient(handler);
        var summarizer = CreateSummarizer(client, new TestLogger<GeminiConversationSummarizer>(), maxRetries: 0);
        var turns = Enumerable.Range(0, 8)
            .Select(index => new AiConversationTurn(
                MessageRole.Speaker,
                $"turn{index}-" + new string('&', 500),
                new string('<', 500),
                new string('"', 500),
                DateTimeOffset.UtcNow.AddMinutes(-index)))
            .ToArray();

        var result = await summarizer.SummarizeAsync(
            new string('<', 4_000),
            "en",
            "tr",
            new string('&', 8_000),
            turns,
            CancellationToken.None);

        Assert.Equal("Bounded summary.", result);
        Assert.NotNull(sentSystemInstruction);
        Assert.NotNull(sentUserPrompt);
        Assert.True(
            sentSystemInstruction!.Length + sentUserPrompt!.Length <= 48_000,
            "The escaped summary request must stay within its deterministic character budget.");
        Assert.Contains("&lt;", sentUserPrompt);
        Assert.Contains("&amp;", sentUserPrompt);
        Assert.DoesNotContain(new string('&', 151), sentUserPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('<', 151), sentUserPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SummarizeAsync_RejectsOversizedModelSummaryWithoutReturningIt()
    {
        var oversized = new string('s', ConversationContextSummary.SummaryMaxLength + 1);
        var handler = new DelegateHandler((_, _) => Task.FromResult(
            JsonResponse(HttpStatusCode.OK, ProviderResponse(oversized))));
        using var client = new HttpClient(handler);
        var summarizer = CreateSummarizer(client, new TestLogger<GeminiConversationSummarizer>(), maxRetries: 0);

        var exception = await Assert.ThrowsAsync<AiProviderException>(
            () => summarizer.SummarizeAsync(
                "active instruction",
                "en",
                "tr",
                null,
                CreateTurns(8),
                CancellationToken.None));

        Assert.Equal("Gemini returned an invalid conversation summary.", exception.Message);
        Assert.DoesNotContain(oversized, exception.ToString(), StringComparison.Ordinal);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task SummarizeAsync_CallerCancellationIsNotSwallowedOrRetried()
    {
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new DelegateHandler(async (_, cancellationToken) =>
        {
            started.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return JsonResponse(HttpStatusCode.OK, ProviderResponse("never"));
        });
        using var client = new HttpClient(handler);
        var summarizer = CreateSummarizer(client, new TestLogger<GeminiConversationSummarizer>(), maxRetries: 1);
        using var cancellation = new CancellationTokenSource();

        var processing = summarizer.SummarizeAsync(
            "active instruction",
            "en",
            "tr",
            null,
            CreateTurns(8),
            cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing);
        Assert.Equal(1, handler.RequestCount);
    }

    private static GeminiConversationSummarizer CreateSummarizer(
        HttpClient client,
        TestLogger<GeminiConversationSummarizer> logger,
        int maxRetries)
        => new(
            client,
            Options.Create(new GeminiOptions
            {
                ApiKey = ApiKey,
                Model = "test-model",
                BaseUrl = "https://gemini.test/",
                TimeoutSeconds = 5,
                MaxRetries = maxRetries,
                InitialRetryDelayMilliseconds = 0
            }),
            logger);

    private static AiConversationTurn[] CreateTurns(int count)
        => Enumerable.Range(0, count)
            .Select(index => new AiConversationTurn(
                MessageRole.Speaker,
                $"older-{index} {PrivateTranscript}",
                $"translation-{index}",
                null,
                DateTimeOffset.UtcNow.AddMinutes(-index)))
            .ToArray();

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string body)
        => new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private static string ProviderResponse(string summary)
        => "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":" +
           JsonSerializer.Serialize(JsonSerializer.Serialize(new { summary })) +
           "}]},\"finishReason\":\"STOP\"}]}";

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        private int _requestCount;

        public int RequestCount => Volatile.Read(ref _requestCount);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            Assert.Equal("TEST_SUMMARY_API_KEY_DO_NOT_LOG", request.Headers.GetValues("x-goog-api-key").Single());
            return send(request, cancellationToken);
        }
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        void ILogger.Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }
}
