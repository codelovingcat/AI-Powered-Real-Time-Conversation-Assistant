using Conversa.Application.Ai;
using Conversa.Infrastructure.Ai.Gemini;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Conversa.Domain.Conversations;

namespace Conversa.Api.Tests.Integration;

public sealed class GeminiAiProviderIntegrationTests
{
    [GeminiIntegrationFact]
    public async Task ProcessAsync_CallsRealGeminiAndReturnsStructuredResponse()
    {
        var apiKey = Environment.GetEnvironmentVariable("Gemini__ApiKey");
        Assert.False(string.IsNullOrWhiteSpace(apiKey), "Gemini__ApiKey must be configured.");

        var options = new GeminiOptions
        {
            ApiKey = apiKey!,
            Model = Environment.GetEnvironmentVariable("Gemini__Model") ?? "gemini-3.8-flash",
            BaseUrl = Environment.GetEnvironmentVariable("Gemini__BaseUrl")
                ?? "https://generativelanguage.googleapis.com/"
        };

        using var httpClient = new HttpClient();
        var provider = new GeminiAiProvider(
            httpClient,
            Options.Create(options),
            NullLogger<GeminiAiProvider>.Instance);

        var request = new AiConversationRequest(
            Instruction: "Translate the speaker's English into natural Turkish and briefly explain the meaning.",
            SourceLanguage: "en",
            TargetLanguage: "tr",
            InputText: "Could you tell me where the meeting room is?",
            InputKind: AiInputKind.HeardSpeech,
            RecentTurns: Array.Empty<AiConversationTurn>());

        var result = await provider.ProcessAsync(request, CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(result.Original));
        Assert.False(string.IsNullOrWhiteSpace(result.Translation));
        Assert.Equal("gemini", provider.Name);
        Assert.True(
            result.Type is AiResponseType.Translation or AiResponseType.Question or AiResponseType.Answer);
    }
}
