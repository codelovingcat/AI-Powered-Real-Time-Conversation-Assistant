using System.Text.Json;
using Conversa.Application.Ai;
using Conversa.Domain.Conversations;
using Conversa.Infrastructure.Ai.Gemini;

namespace Conversa.Api.Tests.Gemini;

public sealed class PromptRegressionFixturesTests
{
    [Fact]
    public async Task SyntheticFixtures_PreservePromptMarkersAndStructuredResponseContract()
    {
        var fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "ai-prompt-regression.json");
        await using var stream = File.OpenRead(fixturePath);
        using var fixtureDocument = await JsonDocument.ParseAsync(stream);
        var root = fixtureDocument.RootElement;

        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(
            GeminiPromptBuilder.PromptVersion,
            root.GetProperty("promptVersion").GetString());

        var cases = root.GetProperty("cases").EnumerateArray().ToArray();
        Assert.Equal(Enum.GetNames<ConversationIntent>().Length, cases.Select(item => item.GetProperty("intent").GetString()).Distinct().Count());

        foreach (var regressionCase in cases)
        {
            var id = regressionCase.GetProperty("id").GetString();
            var request = new AiConversationRequest(
                regressionCase.GetProperty("instruction").GetString()!,
                regressionCase.GetProperty("sourceLanguage").GetString()!,
                regressionCase.GetProperty("targetLanguage").GetString()!,
                regressionCase.GetProperty("inputText").GetString()!,
                Enum.Parse<AiInputKind>(regressionCase.GetProperty("inputKind").GetString()!),
                Array.Empty<AiConversationTurn>());

            var systemInstruction = GeminiPromptBuilder.BuildSystemInstruction(request);
            var userPrompt = GeminiPromptBuilder.BuildUserPrompt(request);

            foreach (var expected in regressionCase.GetProperty("expectedSystemContains").EnumerateArray())
            {
                Assert.Contains(expected.GetString()!, systemInstruction);
            }

            foreach (var expected in regressionCase.GetProperty("expectedUserContains").EnumerateArray())
            {
                Assert.Contains(expected.GetString()!, userPrompt);
            }

            foreach (var forbidden in regressionCase.GetProperty("expectedUserDoesNotContain").EnumerateArray())
            {
                Assert.DoesNotContain(forbidden.GetString()!, userPrompt, StringComparison.Ordinal);
            }

            var responseJson = regressionCase.GetProperty("response").GetRawText();
            var envelope = JsonSerializer.Serialize(new
            {
                candidates = new[]
                {
                    new
                    {
                        content = new
                        {
                            parts = new[]
                            {
                                new { text = responseJson }
                            }
                        },
                        finishReason = "STOP"
                    }
                }
            });

            var parsed = GeminiResponseParser.Parse(envelope);
            var expectedResponse = regressionCase.GetProperty("response");

            Assert.Equal(
                expectedResponse.GetProperty("type").GetString(),
                parsed.Type.ToString().ToLowerInvariant());
            Assert.Equal(
                regressionCase.GetProperty("intent").GetString(),
                ToFixtureIntent(parsed.Intent));
            Assert.Equal(expectedResponse.GetProperty("original").GetString(), parsed.Original);
            Assert.Equal(ReadNullableString(expectedResponse, "translation"), parsed.Translation);
            Assert.Equal(ReadNullableString(expectedResponse, "explanation"), parsed.Explanation);
            Assert.Equal(ReadNullableString(expectedResponse, "suggestedAnswer"), parsed.SuggestedAnswer);
            Assert.Equal(
                ReadNullableString(expectedResponse, "suggestedAnswerTranslation"),
                parsed.SuggestedAnswerTranslation);
            Assert.Equal(expectedResponse.GetProperty("questionDetected").GetBoolean(), parsed.QuestionDetected);
            Assert.Equal(
                expectedResponse.GetProperty("questionDirectedAtUser").GetBoolean(),
                parsed.QuestionDirectedAtUser);

            Assert.False(string.IsNullOrWhiteSpace(id));
        }
    }

    private static string ToFixtureIntent(ConversationIntent intent) => intent switch
    {
        ConversationIntent.Statement => "statement",
        ConversationIntent.QuestionDirectedAtUser => "question_directed_at_user",
        ConversationIntent.QuestionDirectedAtOthers => "question_directed_at_others",
        ConversationIntent.RhetoricalQuestion => "rhetorical_question",
        ConversationIntent.RequestDirectedAtUser => "request_directed_at_user",
        ConversationIntent.RequestDirectedAtOthers => "request_directed_at_others",
        ConversationIntent.UserFormulationRequest => "user_formulation_request",
        _ => "ambiguous"
    };

    private static string? ReadNullableString(JsonElement element, string propertyName)
    {
        var property = element.GetProperty(propertyName);
        return property.ValueKind == JsonValueKind.Null ? null : property.GetString();
    }
}
