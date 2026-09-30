using Conversa.Application.Abstractions.Identity;
using Conversa.Application.Abstractions.Persistence;
using Conversa.Application.Ai;
using Conversa.Application.Common;
using Conversa.Domain.Conversations;

namespace Conversa.Application.Conversations;

public sealed class ConversationAssistant(
    ICurrentUser currentUser,
    IConversationRepository conversations,
    IMessageRepository messages,
    IAiProvider aiProvider,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IConversationAssistant
{
    public const int ContextMessageCount = 12;

    public async Task<MessageDto> ProcessAsync(
        ProcessConversationInputCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Text))
        {
            throw ValidationException.For("text", "Text is required.");
        }

        if (command.Text.Trim().Length > Message.TextMaxLength)
        {
            throw ValidationException.For("text", $"Text cannot exceed {Message.TextMaxLength} characters.");
        }

        var conversation = await conversations.GetByIdAsync(
            command.ConversationId,
            currentUser.UserId,
            cancellationToken);

        if (conversation is null)
        {
            throw new NotFoundException("Conversation was not found.");
        }

        var recent = await messages.ListRecentAsync(conversation.Id, ContextMessageCount, cancellationToken);
        var request = new AiConversationRequest(
            conversation.Instruction,
            conversation.SourceLanguage,
            conversation.TargetLanguage,
            command.Text.Trim(),
            command.InputKind,
            recent.Select(message => new AiConversationTurn(
                message.Role,
                message.OriginalText,
                message.Translation,
                message.SuggestedAnswer,
                message.CreatedAt)).ToArray());

        var result = await aiProvider.ProcessAsync(request, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var role = command.InputKind == AiInputKind.UserFormulationRequest
            ? MessageRole.User
            : MessageRole.Speaker;

        Message message;
        try
        {
            message = Message.CreateFromAssistant(
                conversation.Id,
                role,
                result.Original,
                result.Translation,
                result.Explanation,
                result.SuggestedAnswer,
                result.SuggestedAnswerTranslation,
                result.Type,
                result.QuestionDetected,
                result.QuestionDirectedAtUser,
                aiProvider.Name,
                now);
        }
        catch (ArgumentException exception)
        {
            throw new AiProviderException("The AI provider returned a result that could not be stored.", exception);
        }

        conversation.Touch(now);
        await messages.AddAsync(message, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return message.ToDto();
    }
}
