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
    TimeProvider timeProvider,
    ConversationInputValidator validator) : IConversationService
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 100;
    private const string InstructionUpdatedNote = "Instruction updated.";
    private const string DefaultSourceLanguage = "en";
    private const string DefaultTargetLanguage = "tr";

    public Task<CursorPageDto<ConversationSummaryDto>> ListAsync(
        int limit,
        string? cursor,
        CancellationToken cancellationToken)
        => ListAsync(limit, cursor, null, null, null, cancellationToken);

    public async Task<CursorPageDto<ConversationSummaryDto>> ListAsync(
        int limit,
        string? cursor,
        string? search,
        DateTimeOffset? updatedFrom,
        DateTimeOffset? updatedTo,
        CancellationToken cancellationToken)
    {
        validator.ValidateConversationList(search, updatedFrom, updatedTo);

        var bounded = limit <= 0 ? DefaultPageSize : Math.Min(limit, MaxPageSize);
        (DateTimeOffset Timestamp, Guid Id)? decoded = cursor is null
            ? null
            : PaginationCursor.Decode(cursor);
        var normalizedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();

        var items = await conversations.ListByUserPageAsync(
            currentUser.UserId,
            bounded + 1,
            normalizedSearch,
            updatedFrom,
            updatedTo,
            decoded?.Timestamp,
            decoded?.Id,
            cancellationToken);

        var hasMore = items.Count > bounded;
        var pageItems = hasMore
            ? items.Take(bounded).ToArray()
            : items.ToArray();

        var nextCursor = hasMore && pageItems.Length > 0
            ? PaginationCursor.Encode(
                pageItems[^1].UpdatedAt,
                pageItems[^1].Id)
            : null;

        return new CursorPageDto<ConversationSummaryDto>(
            pageItems.Select(item => item.ToSummary()).ToArray(),
            nextCursor);
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
        validator.ValidateCreate(command);

        Conversation conversation;
        try
        {
            conversation = Conversation.Create(
                currentUser.UserId,
                string.IsNullOrWhiteSpace(command.Title) ? "New conversation" : command.Title,
                command.Instruction,
                string.IsNullOrWhiteSpace(command.SourceLanguage) ? DefaultSourceLanguage : command.SourceLanguage,
                string.IsNullOrWhiteSpace(command.TargetLanguage) ? DefaultTargetLanguage : command.TargetLanguage,
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
        validator.ValidateUpdate(command);

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
                Message.CreateSystemNote(conversation.Id, InstructionUpdatedNote, now),
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

    public async Task<CursorPageDto<MessageDto>> ListMessagesAsync(
        Guid conversationId,
        int limit,
        string? cursor,
        CancellationToken cancellationToken)
    {
        _ = await RequireAsync(conversationId, cancellationToken);

        var bounded = limit <= 0 ? DefaultPageSize : Math.Min(limit, MaxPageSize);
        (DateTimeOffset Timestamp, Guid Id)? decoded = cursor is null
            ? null
            : PaginationCursor.Decode(cursor);

        var items = await messages.ListByConversationPageAsync(
            conversationId,
            bounded + 1,
            decoded?.Timestamp,
            decoded?.Id,
            cancellationToken);

        var hasMore = items.Count > bounded;
        var pageItems = hasMore
            ? items.Take(bounded).ToArray()
            : items.ToArray();

        Array.Reverse(pageItems);

        var nextCursor = hasMore && pageItems.Length > 0
            ? PaginationCursor.Encode(
                pageItems[0].CreatedAt,
                pageItems[0].Id)
            : null;

        return new CursorPageDto<MessageDto>(
            pageItems.Select(item => item.ToDto()).ToArray(),
            nextCursor);
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
