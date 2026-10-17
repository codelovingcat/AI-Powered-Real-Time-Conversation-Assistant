using Conversa.Application.Ai;
using Conversa.Application.Common;

namespace Conversa.Infrastructure.Ai.Gemini;

/// <summary>
/// Validates response invariants after intent policy normalization and before a result
/// can be returned to the application layer for persistence.
/// </summary>
internal static class GeminiResponseContractValidator
{
    public static void Validate(AiAssistantResponse response, AiInputKind inputKind)
    {
        var hasSuggestedAnswer = !string.IsNullOrWhiteSpace(response.SuggestedAnswer);
        var hasSuggestedAnswerTranslation = !string.IsNullOrWhiteSpace(response.SuggestedAnswerTranslation);

        if (hasSuggestedAnswer != hasSuggestedAnswerTranslation)
        {
            throw new AiProviderException("Gemini returned an incomplete suggested-answer pair.");
        }

        if (inputKind == AiInputKind.UserFormulationRequest)
        {
            if (response.Type != AiResponseType.Instruction || !hasSuggestedAnswer)
            {
                throw new AiProviderException("Gemini returned no formulated speech for a user formulation request.");
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(response.Translation))
        {
            throw new AiProviderException("Gemini returned no translation for heard speech.");
        }
    }
}
