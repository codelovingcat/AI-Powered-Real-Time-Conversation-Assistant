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

public sealed class GeminiAiProviderTests
{
    private const string TestApiKey = "TEST_ONLY_API_KEY_DO_NOT_LOG";
    private const string PrivatePrompt = "TEST_ONLY_PRIVATE_PROMPT_DO_NOT_LOG";
    private const string PrivateProviderBody = "TEST_ONLY_RAW_PROVIDER_BODY_DO_NOT_LOG";

    [Fact]
    public void Parser_ValidGeminiEnvelope_ReturnsStructuredTranslation()
    {
        var result = GeminiResponseParser.Parse(SuccessResponse(ValidModelJson));

        Assert.Equal(AiResponseType.Translation, result.Type);
        Assert.Equal("Hello there.", result.Original);
        Assert.Equal("Merhaba.", result.Translation);
        Assert.False(result.QuestionDetected);
        Assert.False(result.QuestionDirectedAtUser);
    }

    [Fact]
    public void Parser_MalformedModelJson_ThrowsProviderNeutralError()
    {
        var exception = Assert.Throws<AiProviderException>(
            () => GeminiResponseParser.Parse(SuccessResponse("{ malformed private provider content")));

        Assert.Equal("Gemini returned a response that was not valid JSON.", exception.Message);
        Assert.DoesNotContain(PrivatePrompt, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Parser_EmptyCandidateList_IsRejected()
    {
        var exception = Assert.Throws<AiProviderException>(
            () => GeminiResponseParser.Parse("{\"candidates\":[]}"));

        Assert.Equal("Gemini returned no candidates.", exception.Message);
    }

    [Fact]
    public async Task ProcessAsync_RetriesTransient429_ThenReturnsStructuredResult()
    {
        var responses = new Queue<HttpResponseMessage>(
        [
            JsonResponse(HttpStatusCode.TooManyRequests, "{\"error\":{\"status\":\"RESOURCE_EXHAUSTED\"}}"),
            JsonResponse(HttpStatusCode.OK, SuccessResponse(ValidModelJson))
        ]);
        var handler = new DelegateHandler((_, _) => Task.FromResult(responses.Dequeue()));
        using var client = new HttpClient(handler);
        var logger = new TestLogger<GeminiAiProvider>();
        var provider = CreateProvider(client, logger, maxRetries: 1);

        var result = await provider.ProcessAsync(CreateRequest(), CancellationToken.None);

        Assert.Equal("Merhaba.", result.Translation);
        Assert.Equal(2, handler.RequestCount);
        Assert.Contains(logger.Messages, message => message.Contains("status 429", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProcessAsync_QuotaExceeded429_IsNotRetried_AndDoesNotLogSensitivePayloads()
    {
        var body = "{\"error\":{\"code\":429,\"status\":\"RESOURCE_EXHAUSTED\",\"message\":\"" +
                   PrivateProviderBody + " " + PrivatePrompt + " " + TestApiKey +
                   "\",\"details\":[{\"reason\":\"QUOTA_EXCEEDED\"}]}}";
        var handler = new DelegateHandler((_, _) => Task.FromResult(
            JsonResponse(HttpStatusCode.TooManyRequests, body)));
        using var client = new HttpClient(handler);
        var logger = new TestLogger<GeminiAiProvider>();
        var provider = CreateProvider(client, logger, maxRetries: 3);

        var exception = await Assert.ThrowsAsync<AiProviderException>(
            () => provider.ProcessAsync(CreateRequest(), CancellationToken.None));

        Assert.Equal(1, handler.RequestCount);
        Assert.DoesNotContain(PrivateProviderBody, exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(PrivatePrompt, exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(TestApiKey, exception.ToString(), StringComparison.Ordinal);
        var logs = string.Join(Environment.NewLine, logger.Messages);
        Assert.DoesNotContain(PrivateProviderBody, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(PrivatePrompt, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(TestApiKey, logs, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_ServiceUnavailable_StopsAtConfiguredRetryBound()
    {
        var handler = new DelegateHandler((_, _) => Task.FromResult(
            JsonResponse(HttpStatusCode.ServiceUnavailable, "{\"error\":{\"status\":\"UNAVAILABLE\"}}")));
        using var client = new HttpClient(handler);
        var provider = CreateProvider(client, new TestLogger<GeminiAiProvider>(), maxRetries: 2);

        await Assert.ThrowsAsync<AiProviderException>(
            () => provider.ProcessAsync(CreateRequest(), CancellationToken.None));

        Assert.Equal(3, handler.RequestCount);
    }

    [Fact]
    public async Task ProcessAsync_TimeoutsAreRetriedOnlyUpToConfiguredBound()
    {
        var handler = new DelegateHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return JsonResponse(HttpStatusCode.OK, SuccessResponse(ValidModelJson));
        });
        using var client = new HttpClient(handler);
        var provider = CreateProvider(
            client,
            new TestLogger<GeminiAiProvider>(),
            maxRetries: 1,
            timeoutSeconds: 1);

        var exception = await Assert.ThrowsAsync<AiProviderException>(
            () => provider.ProcessAsync(CreateRequest(), CancellationToken.None));

        Assert.Equal("Gemini request timed out.", exception.Message);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task ProcessAsync_CallerCancellation_IsNotRetried()
    {
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new DelegateHandler(async (_, cancellationToken) =>
        {
            started.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return JsonResponse(HttpStatusCode.OK, SuccessResponse(ValidModelJson));
        });
        using var client = new HttpClient(handler);
        var provider = CreateProvider(client, new TestLogger<GeminiAiProvider>(), maxRetries: 3);
        using var cancellation = new CancellationTokenSource();

        var processing = provider.ProcessAsync(CreateRequest(), cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task ProcessAsync_InvalidStructuredOutput_IsRetriedOnlyWithinConfiguredBound()
    {
        var malformed = SuccessResponse("not valid model json: " + PrivateProviderBody);
        var handler = new DelegateHandler((_, _) => Task.FromResult(
            JsonResponse(HttpStatusCode.OK, malformed)));
        using var client = new HttpClient(handler);
        var logger = new TestLogger<GeminiAiProvider>();
        var provider = CreateProvider(client, logger, maxRetries: 1);

        var exception = await Assert.ThrowsAsync<AiProviderException>(
            () => provider.ProcessAsync(CreateRequest(), CancellationToken.None));

        Assert.Equal(2, handler.RequestCount);
        Assert.Equal("Gemini returned a response that was not valid JSON.", exception.Message);
        Assert.DoesNotContain(PrivateProviderBody, string.Join(Environment.NewLine, logger.Messages), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_FinalTransportFailure_IsProviderNeutral()
    {
        var handler = new DelegateHandler((_, _) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("synthetic transport failure")));
        using var client = new HttpClient(handler);
        var provider = CreateProvider(client, new TestLogger<GeminiAiProvider>(), maxRetries: 0);

        var exception = await Assert.ThrowsAsync<AiProviderException>(
            () => provider.ProcessAsync(CreateRequest(), CancellationToken.None));

        Assert.Equal("Gemini transport request failed.", exception.Message);
        Assert.IsType<HttpRequestException>(exception.InnerException);
        Assert.Equal(1, handler.RequestCount);
    }

    private static GeminiAiProvider CreateProvider(
        HttpClient client,
        TestLogger<GeminiAiProvider> logger,
        int maxRetries,
        int timeoutSeconds = 1)
        => new(
            client,
            Options.Create(new GeminiOptions
            {
                ApiKey = TestApiKey,
                Model = "test-model",
                BaseUrl = "https://gemini.test/",
                TimeoutSeconds = timeoutSeconds,
                MaxRetries = maxRetries,
                InitialRetryDelayMilliseconds = 0
            }),
            logger);

    private static AiConversationRequest CreateRequest()
        => new(
            Instruction: "Handle this synthetic instruction without logging it: " + PrivatePrompt,
            SourceLanguage: "en",
            TargetLanguage: "tr",
            InputText: "Synthetic speech only.",
            InputKind: AiInputKind.HeardSpeech,
            RecentTurns: Array.Empty<AiConversationTurn>());

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string body)
        => new(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private static string SuccessResponse(string modelJson)
        => "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":" +
           JsonSerializer.Serialize(modelJson) +
           "}]},\"finishReason\":\"STOP\"}]}";

    private const string ValidModelJson =
        "{\"type\":\"translation\",\"original\":\"Hello there.\",\"translation\":\"Merhaba.\"," +
        "\"explanation\":null,\"suggestedAnswer\":null,\"suggestedAnswerTranslation\":null," +
        "\"questionDetected\":false,\"questionDirectedAtUser\":false}";

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
            return send(request, cancellationToken);
        }
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            where TState : notnull
        {
            Messages.Add(formatter(state, exception));
        }
    }
}
