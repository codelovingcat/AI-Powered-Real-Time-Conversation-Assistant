namespace Conversa.Application.Conversations;

public interface IConversationService
{
    Task<CursorPageDto<ConversationSummaryDto>> ListAsync(
        int limit,
        string? cursor,
        CancellationToken cancellationToken);

    Task<ConversationSummaryDto> GetAsync(Guid conversationId, CancellationToken cancellationToken);

    Task<ConversationSummaryDto> CreateAsync(CreateConversationCommand command, CancellationToken cancellationToken);

    Task<ConversationSummaryDto> UpdateAsync(UpdateConversationCommand command, CancellationToken cancellationToken);

    Task DeleteAsync(Guid conversationId, CancellationToken cancellationToken);

    Task<CursorPageDto<MessageDto>> ListMessagesAsync(
        Guid conversationId,
        int limit,
        string? cursor,
        CancellationToken cancellationToken);
}
