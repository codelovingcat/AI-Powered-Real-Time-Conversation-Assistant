namespace Conversa.Application.Ai;

/// <summary>
/// Provider-neutral conversation assistant. Implementations live in Infrastructure.
/// </summary>
public interface IAiProvider
{
    string Name { get; }

    Task<AiAssistantResponse> ProcessAsync(AiConversationRequest request, CancellationToken cancellationToken);
}
