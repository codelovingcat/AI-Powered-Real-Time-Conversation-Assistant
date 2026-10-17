using System.Text.Json;
using Conversa.Application.Ai;
using Conversa.Domain.Conversations;
using Conversa.Infrastructure.Ai.Gemini;

namespace Conversa.Api.Tests.Gemini;

public sealed class PromptRegressionFixtureTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void CurrentPromptVersions_AreExplicitAndDistinct()
    {
        Assert.Equal("assistant-v1", AiPromptVersions.Assistant);
        Assert.Equal("context-summary-v1", AiPromptVersions.ContextSummary);
        Assert.NotEqual(AiPromptVersions.Assistant, AiPromptVersions.ContextSummary);
    }

    [Fact]
    public void SyntheticRegressionFixtures_PinPromptVersion_PromptComposition_AndStructuredOutput()
    {
        var fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "Gemini",
            "Fixtures",
            "assistant-regression.json");
        Assert.True(File.Exists(fixturePath), $"Prompt regression fixture missing: {fixturePath}");

        var fixtures = JsonSerializer.Deserialize<FixtureSet>(
            File.ReadAllText(fixturePath),
            JsonOptions);
        Assert.NotNull(fixtures);
        Assert.Equal("assistant-regression-v1", fixtures!.FixtureSetVersion);
        Assert.Equal(AiPromptVersions.Assistant, fixtures.PromptVersion);
        Assert.Equal("synthetic-only", fixtures.DataSource);
        Assert.NotEmpty(fixtures.Cases);

        foreach (var testCase in fixtures.Cases)
        {
            Assert.False(string.IsNullOrWhiteSpace(testCase.Id));

            var request = new AiConversationRequest(
                testCase.Instruction,
                testCase.SourceLanguage,
                testCase.TargetLanguage,
                testCase.InputText,
                Enum.Parse<AiInputKind>(testCase.InputKind),
                Array.Empty<AiConversationTurn>());

            var systemInstruction = GeminiPromptBuilder.BuildSystemInstruction(request);
            var userPrompt = GeminiPromptBuilder.BuildUserPrompt(request);

            foreach (var marker in testCase.ExpectedSystemMarkers)
            {
                Assert.True(
                    systemInstruction.Contains(marker, StringComparison.Ordinal),
                    $"Fixture '{testCase.Id}' expected system prompt marker '{marker}'.");
            }

            foreach (var marker in testCase.ExpectedUserMarkers)
            {
                Assert.True(
                    userPrompt.Contains(marker, StringComparison.Ordinal),
                    $"Fixture '{testCase.Id}' expected user prompt marker '{marker}'.");
            }

            var parsed = GeminiResponseParser.ParseModelJson(
                JsonSerializer.Serialize(testCase.ModelResponse, JsonOptions));
            Assert.Equal(testCase.Expected.Type, parsed.Type.ToString());
            Assert.Equal(testCase.Expected.Original, parsed.Original);
            Assert.Equal(testCase.Expected.Translation, parsed.Translation);
            Assert.Equal(testCase.Expected.Explanation, parsed.Explanation);
            Assert.Equal(testCase.Expected.SuggestedAnswer, parsed.SuggestedAnswer);
            Assert.Equal(testCase.Expected.SuggestedAnswerTranslation, parsed.SuggestedAnswerTranslation);
            Assert.Equal(testCase.Expected.QuestionDetected, parsed.QuestionDetected);
            Assert.Equal(testCase.Expected.QuestionDirectedAtUser, parsed.QuestionDirectedAtUser);
        }
    }

    private sealed record FixtureSet(
        string FixtureSetVersion,
        string PromptVersion,
        string DataSource,
        FixtureCase[] Cases);

    private sealed record FixtureCase(
        string Id,
        string Instruction,
        string SourceLanguage,
        string TargetLanguage,
        string InputKind,
        string InputText,
        string[] ExpectedSystemMarkers,
        string[] ExpectedUserMarkers,
        JsonElement ModelResponse,
        ExpectedResponse Expected);

    private sealed record ExpectedResponse(
        string Type,
        string Original,
        string? Translation,
        string? Explanation,
        string? SuggestedAnswer,
        string? SuggestedAnswerTranslation,
        bool QuestionDetected,
        bool QuestionDirectedAtUser);
}
