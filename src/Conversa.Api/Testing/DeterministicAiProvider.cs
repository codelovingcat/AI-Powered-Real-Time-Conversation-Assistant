using Conversa.Application.Ai;
using Conversa.Domain.Conversations;

namespace Conversa.Api.Testing;

internal sealed class DeterministicAiProvider : IAiProvider
{
    public string Name => "e2e-test";

    public Task<AiAssistantResponse> ProcessAsync(
        AiConversationRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (request.InputKind == AiInputKind.UserFormulationRequest)
        {
            return Task.FromResult(
                new AiAssistantResponse(
                    AiResponseType.Answer,
                    request.InputText,
                    "Deterministic E2E meaning.",
                    "Deterministic E2E explanation.",
                    "This is a deterministic E2E reply.",
                    "Bu, deterministik bir E2E yanıtıdır.",
                    false,
                    false));
        }

        return Task.FromResult(
            new AiAssistantResponse(
                AiResponseType.Translation,
                request.InputText,
                "Deterministic E2E translation.",
                "Deterministic E2E explanation.",
                "This is a deterministic E2E reply.",
                "Bu, deterministik bir E2E yanıtıdır.",
                false,
                false));
    }
}
