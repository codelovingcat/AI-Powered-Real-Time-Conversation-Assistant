using Conversa.Application.Ai;
using Conversa.Domain.Conversations;

namespace Conversa.Api.Tests.Ai;

public sealed class AiAssistantResponsePreferencesPolicyTests
{
    [Fact]
    public void Apply_WhenSuggestionsDisabled_SuppressesUnsolicitedReplyForHeardSpeech()
    {
        var response = new AiAssistantResponse(
            AiResponseType.Answer,
            "Could you send it today?",
            "Bugün gönderebilir misiniz?",
            "The speaker is making a request.",
            "Certainly, I'll send it today.",
            "Elbette, bugün göndereceğim.",
            QuestionDetected: false,
            QuestionDirectedAtUser: false,
            Intent: ConversationIntent.RequestDirectedAtUser);

        var result = AiAssistantResponsePreferencesPolicy.Apply(
            response,
            AiInputKind.HeardSpeech,
            new AssistantResponsePreferences("balanced", "contextual", "balanced", false));

        Assert.Equal(AiResponseType.Translation, result.Type);
        Assert.Equal("Bugün gönderebilir misiniz?", result.Translation);
        Assert.Null(result.SuggestedAnswer);
        Assert.Null(result.SuggestedAnswerTranslation);
    }

    [Fact]
    public void Apply_WhenSuggestionsDisabled_DoesNotBlockExplicitFormulationRequest()
    {
        var response = new AiAssistantResponse(
            AiResponseType.Instruction,
            "Bunu tekrar açıklayabilir misiniz?",
            null,
            "A polite request for clarification.",
            "Could you explain that again?",
            "Bunu tekrar açıklayabilir misiniz?",
            QuestionDetected: false,
            QuestionDirectedAtUser: false,
            Intent: ConversationIntent.UserFormulationRequest);

        var result = AiAssistantResponsePreferencesPolicy.Apply(
            response,
            AiInputKind.UserFormulationRequest,
            new AssistantResponsePreferences("concise", "formal", "minimal", false));

        Assert.Equal(AiResponseType.Instruction, result.Type);
        Assert.Equal("Could you explain that again?", result.SuggestedAnswer);
        Assert.Null(result.Explanation);
    }
}
