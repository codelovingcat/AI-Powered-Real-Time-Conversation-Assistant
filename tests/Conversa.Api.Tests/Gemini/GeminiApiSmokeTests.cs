using Conversa.Application.Ai;
using Conversa.Infrastructure.Ai.Gemini;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Conversa.Api.Tests.Gemini;

public sealed class GeminiApiSmokeTests
{
    [Fact]
    public async Task Gemini_can_process_a_real_assistant_request()
    {
        var apiKey = Environment.GetEnvironmentVariable("Gemini__ApiKey");

        Assert.False(
            string.IsNullOrWhiteSpace(apiKey),
            "Gemini__ApiKey must be provided by the GitHub Actions secret.");

        using var httpClient = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

        var options = Options.Create(new GeminiOptions
        {
            ApiKey = apiKey!,
            Model = "gemini-2.5-flash",
            BaseUrl = "https://generativelanguage.googleapis.com/",
            TimeoutSeconds = 45,
            MaxRetries = 1,
            InitialRetryDelayMilliseconds = 250
        });

        var provider = new GeminiAiProvider(
            httpClient,
            options,
            NullLogger<GeminiAiProvider>.Instance);

        var request = new AiConversationRequest(
            Instruction: "Translate the speaker's English into natural and accurate Turkish. Only translate and explain what is being said.",
            SourceLanguage: "en",
            TargetLanguage: "tr",
            InputText: "Hello, how are you today?",
            InputKind: AiInputKind.HeardSpeech,
            RecentTurns: Array.Empty<AiConversationTurn>());

        var result = await provider.ProcessAsync(request, CancellationToken.None);

        Assert.Equal("gemini", provider.Name);
        Assert.Equal("Hello, how are you today?", result.Original);
        Assert.False(
            string.IsNullOrWhiteSpace(result.Translation),
            "Gemini must return a non-empty translation.");
    }
}
