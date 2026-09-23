namespace Conversa.Application.Conversations;

public interface IConversationService
{
    Task<IReadOnlyList<ConversationSummaryDto>> ListAsync(int take, CancellationToken cancellationToken);

    Task<ConversationSummaryDto> GetAsync(Guid conversationId, CancellationToken cancellationToken);

    Task<ConversationSummaryDto> CreateAsync(CreateConversationCommand command, CancellationToken cancellationToken);

    Task<ConversationSummaryDto> UpdateAsync(UpdateConversationCommand command, CancellationToken cancellationToken);

    Task DeleteAsync(Guid conversationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<MessageDto>> ListMessagesAsync(Guid conversationId, CancellationToken cancellationToken);
}
