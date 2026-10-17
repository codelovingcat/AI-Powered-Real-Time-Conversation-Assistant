using Conversa.Domain.Conversations;

namespace Conversa.Application.Ai;

public static class AiAssistantResponsePreferencesPolicy
{
    public static AiAssistantResponse Apply(
        AiAssistantResponse response,
        AiInputKind inputKind,
        AssistantResponsePreferences preferences)
    {
        var result = response;

        if (preferences.ExplanationLevel == "minimal")
        {
            result = result with { Explanation = null };
        }

        // A formulation request explicitly asks Conversa to produce speech. It must
        // remain answerable even when unsolicited reply suggestions are disabled.
        if (inputKind == AiInputKind.UserFormulationRequest || preferences.SuggestReplies)
        {
            return result;
        }

        return result with
        {
            Type = result.Intent == ConversationIntent.QuestionDirectedAtUser
                ? AiResponseType.Question
                : AiResponseType.Translation,
            SuggestedAnswer = null,
            SuggestedAnswerTranslation = null
        };
    }
}
