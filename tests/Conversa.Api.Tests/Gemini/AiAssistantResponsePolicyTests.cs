using Conversa.Application.Ai;
using Conversa.Application.Common;
using Conversa.Domain.Conversations;

namespace Conversa.Api.Tests.Gemini;

public sealed class AiAssistantResponsePolicyTests
{
    [Fact]
    public void Normalize_QuestionDirectedAtOthers_SuppressesSuggestedReplyAndKeepsDetection()
    {
        var result = AiAssistantResponsePolicy.Normalize(
            CreateResponse(ConversationIntent.QuestionDirectedAtOthers, "Not my issue.", true, false),
            AiInputKind.HeardSpeech);

        Assert.Equal(AiResponseType.Translation, result.Type);
        Assert.Equal(ConversationIntent.QuestionDirectedAtOthers, result.Intent);
        Assert.True(result.QuestionDetected);
        Assert.False(result.QuestionDirectedAtUser);
        Assert.Null(result.SuggestedAnswer);
        Assert.Null(result.SuggestedAnswerTranslation);
        Assert.Equal("Merhaba mı?", result.Translation);
    }

    [Theory]
    [InlineData(ConversationIntent.Statement)]
    [InlineData(ConversationIntent.RhetoricalQuestion)]
    [InlineData(ConversationIntent.RequestDirectedAtOthers)]
    [InlineData(ConversationIntent.Ambiguous)]
    public void Normalize_NonDirectedOrAmbiguousIntent_NeutralizesConflictingAnswer(ConversationIntent intent)
    {
        var result = AiAssistantResponsePolicy.Normalize(
            CreateResponse(intent, "Unsolicited answer.", true, true),
            AiInputKind.HeardSpeech);

        Assert.Null(result.SuggestedAnswer);
        Assert.Null(result.SuggestedAnswerTranslation);
        Assert.False(result.QuestionDirectedAtUser);
        Assert.Equal(intent == ConversationIntent.RhetoricalQuestion, result.QuestionDetected);
        Assert.Equal(AiResponseType.Translation, result.Type);
    }

    [Fact]
    public void Normalize_QuestionDirectedAtUser_PreservesSuggestedReplyAndCorrectsFlags()
    {
        var result = AiAssistantResponsePolicy.Normalize(
            CreateResponse(ConversationIntent.QuestionDirectedAtUser, "Yes, certainly.", false, false),
            AiInputKind.HeardSpeech);

        Assert.True(result.QuestionDetected);
        Assert.True(result.QuestionDirectedAtUser);
        Assert.Equal("Yes, certainly.", result.SuggestedAnswer);
        Assert.Equal(AiResponseType.Question, result.Type);
    }

    [Fact]
    public void Normalize_RequestDirectedAtUser_PreservesReplyWithoutQuestionFlags()
    {
        var result = AiAssistantResponsePolicy.Normalize(
            CreateResponse(ConversationIntent.RequestDirectedAtUser, "Of course.", true, true),
            AiInputKind.HeardSpeech);

        Assert.False(result.QuestionDetected);
        Assert.False(result.QuestionDirectedAtUser);
        Assert.Equal("Of course.", result.SuggestedAnswer);
        Assert.Equal(AiResponseType.Answer, result.Type);
    }

    [Fact]
    public void Normalize_UserFormulationInput_OverridesModelIntent()
    {
        var result = AiAssistantResponsePolicy.Normalize(
            CreateResponse(ConversationIntent.Ambiguous, "Could we move the meeting?", true, true),
            AiInputKind.UserFormulationRequest);

        Assert.Equal(ConversationIntent.UserFormulationRequest, result.Intent);
        Assert.Equal(AiResponseType.Instruction, result.Type);
        Assert.False(result.QuestionDetected);
        Assert.False(result.QuestionDirectedAtUser);
        Assert.Equal("Could we move the meeting?", result.SuggestedAnswer);
    }

    [Fact]
    public void Normalize_NonDirectedResponseWithoutTranslation_FailsClosed()
    {
        var response = CreateResponse(
            ConversationIntent.Ambiguous,
            "Unsolicited answer.",
            questionDetected: false,
            directedAtUser: false,
            translation: null);
        var exception = Assert.Throws<AiProviderException>(
            () => AiAssistantResponsePolicy.Normalize(response, AiInputKind.HeardSpeech));

        Assert.Equal("Gemini returned no safe translation for a non-directed utterance.", exception.Message);
    }

    private static AiAssistantResponse CreateResponse(
        ConversationIntent intent,
        string? suggestedAnswer,
        bool questionDetected,
        bool directedAtUser,
        string? translation = "Merhaba mı?")
        => new(
            AiResponseType.Question,
            "Hello?",
            translation,
            null,
            suggestedAnswer,
            suggestedAnswer is null ? null : "Evet.",
            questionDetected,
            directedAtUser,
            Intent: intent);
}
