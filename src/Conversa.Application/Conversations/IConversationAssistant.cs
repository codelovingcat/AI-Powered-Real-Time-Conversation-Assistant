namespace Conversa.Application.Conversations;

public interface IConversationAssistant
{
    Task<MessageDto> ProcessAsync(ProcessConversationInputCommand command, CancellationToken cancellationToken);
}
