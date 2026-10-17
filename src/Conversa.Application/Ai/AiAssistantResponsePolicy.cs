using Conversa.Application.Common;
using Conversa.Domain.Conversations;

namespace Conversa.Application.Ai;

/// <summary>
/// Applies conservative output rules after model parsing. A suggested answer is retained
/// only when the classified intent affirmatively authorizes a reply to the user.
/// </summary>
public static class AiAssistantResponsePolicy
{
    public static AiAssistantResponse Normalize(
        AiAssistantResponse response,
        AiInputKind inputKind)
    {
        if (inputKind == AiInputKind.UserFormulationRequest)
        {
            return response with
            {
                Type = AiResponseType.Instruction,
                Intent = ConversationIntent.UserFormulationRequest,
                QuestionDetected = false,
                QuestionDirectedAtUser = false
            };
        }

        return response.Intent switch
        {
            ConversationIntent.QuestionDirectedAtUser => response with
            {
                Type = AiResponseType.Question,
                QuestionDetected = true,
                QuestionDirectedAtUser = true
            },
            ConversationIntent.RequestDirectedAtUser => response with
            {
                Type = string.IsNullOrWhiteSpace(response.SuggestedAnswer)
                    ? AiResponseType.Translation
                    : AiResponseType.Answer,
                QuestionDetected = false,
                QuestionDirectedAtUser = false
            },
            ConversationIntent.QuestionDirectedAtOthers => Neutralize(response, response.Intent, true),
            ConversationIntent.RhetoricalQuestion => Neutralize(response, response.Intent, true),
            ConversationIntent.RequestDirectedAtOthers => Neutralize(response, response.Intent, false),
            ConversationIntent.Statement => Neutralize(response, response.Intent, false),
            _ => Neutralize(response, ConversationIntent.Ambiguous, false)
        };
    }

    private static AiAssistantResponse Neutralize(
        AiAssistantResponse response,
        ConversationIntent intent,
        bool questionDetected)
    {
        if (string.IsNullOrWhiteSpace(response.Translation))
        {
            throw new AiProviderException(
                "Gemini returned no safe translation for a non-directed utterance.");
        }

        return response with
        {
            Type = AiResponseType.Translation,
            Intent = intent,
            SuggestedAnswer = null,
            SuggestedAnswerTranslation = null,
            QuestionDetected = questionDetected,
            QuestionDirectedAtUser = false
        };
    }
}
