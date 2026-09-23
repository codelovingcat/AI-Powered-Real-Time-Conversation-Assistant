using Conversa.Application.Abstractions.Identity;
using Conversa.Application.Abstractions.Persistence;
using Conversa.Application.Common;
using Conversa.Domain.Conversations;

namespace Conversa.Application.Conversations;

public sealed class ConversationService(
    ICurrentUser currentUser,
    IConversationRepository conversations,
    IMessageRepository messages,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IConversationService
{
    public const int DefaultListSize = 50;
    public const int MaxListSize = 200;
    public const int MaxMessages = 1000;

    public async Task<IReadOnlyList<ConversationSummaryDto>> ListAsync(int take, CancellationToken cancellationToken)
    {
        var bounded = take <= 0 ? DefaultListSize : Math.Min(take, MaxListSize);
        var items = await conversations.ListByUserAsync(currentUser.UserId, bounded, cancellationToken);
        return items.Select(item => item.ToSummary()).ToArray();
    }

    public async Task<ConversationSummaryDto> GetAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var conversation = await RequireAsync(conversationId, cancellationToken);
        return conversation.ToSummary();
    }

    public async Task<ConversationSummaryDto> CreateAsync(
        CreateConversationCommand command,
        CancellationToken cancellationToken)
    {
        Conversation conversation;
        try
        {
            conversation = Conversation.Create(
                currentUser.UserId,
                string.IsNullOrWhiteSpace(command.Title) ? "New conversation" : command.Title,
                command.Instruction,
                string.IsNullOrWhiteSpace(command.SourceLanguage) ? "en" : command.SourceLanguage,
                string.IsNullOrWhiteSpace(command.TargetLanguage) ? "tr" : command.TargetLanguage,
                timeProvider.GetUtcNow());
        }
        catch (ArgumentException exception)
        {
            throw ToValidation(exception);
        }

        await conversations.AddAsync(conversation, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return conversation.ToSummary();
    }

    public async Task<ConversationSummaryDto> UpdateAsync(
        UpdateConversationCommand command,
        CancellationToken cancellationToken)
    {
        if (command.Title is null
            && command.Instruction is null
            && command.SourceLanguage is null
            && command.TargetLanguage is null)
        {
            throw ValidationException.For("request", "Provide at least one field to update.");
        }

        var conversation = await RequireAsync(command.ConversationId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var instructionChanged = false;

        try
        {
            if (command.Title is not null)
            {
                conversation.Rename(command.Title, now);
            }

            if (command.Instruction is not null && !string.Equals(
                    command.Instruction.Trim(),
                    conversation.Instruction,
                    StringComparison.Ordinal))
            {
                conversation.ChangeInstruction(command.Instruction, now);
                instructionChanged = true;
            }

            if (command.SourceLanguage is not null || command.TargetLanguage is not null)
            {
                conversation.ChangeLanguages(
                    command.SourceLanguage ?? conversation.SourceLanguage,
                    command.TargetLanguage ?? conversation.TargetLanguage,
                    now);
            }
        }
        catch (ArgumentException exception)
        {
            throw ToValidation(exception);
        }

        if (instructionChanged)
        {
            await messages.AddAsync(
                Message.CreateSystemNote(conversation.Id, "Instruction updated.", now),
                cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return conversation.ToSummary();
    }

    public async Task DeleteAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var conversation = await RequireAsync(conversationId, cancellationToken);
        conversations.Remove(conversation);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MessageDto>> ListMessagesAsync(
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        _ = await RequireAsync(conversationId, cancellationToken);
        var items = await messages.ListByConversationAsync(conversationId, MaxMessages, cancellationToken);
        return items.Select(item => item.ToDto()).ToArray();
    }

    private static ValidationException ToValidation(ArgumentException exception)
    {
        var message = exception.Message;
        var marker = " (Parameter";
        var cut = message.IndexOf(marker, StringComparison.Ordinal);
        if (cut > 0)
        {
            message = message[..cut];
        }

        return ValidationException.For(exception.ParamName ?? "request", message);
    }

    private async Task<Conversation> RequireAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var conversation = await conversations.GetByIdAsync(conversationId, currentUser.UserId, cancellationToken);
        return conversation ?? throw new NotFoundException("Conversation was not found.");
    }
}
