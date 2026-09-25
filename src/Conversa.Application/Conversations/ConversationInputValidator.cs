using Conversa.Application.Ai;
using Conversa.Application.Common;
using Conversa.Domain.Common;
using Conversa.Domain.Conversations;

namespace Conversa.Application.Conversations;

public sealed class ConversationInputValidator
{
    public const int MaxRequestBodyBytes = 64 * 1024;
    public const int MaxAssistantRequestBodyBytes = 32 * 1024;

    public void ValidateCreate(CreateConversationCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var errors = new Dictionary<string, string[]>();

        ValidateOptionalText(
            errors,
            "title",
            command.Title,
            Conversation.TitleMaxLength);

        ValidateRequiredText(
            errors,
            "instruction",
            command.Instruction,
            Conversation.InstructionMaxLength);

        ValidateOptionalLanguage(errors, "sourceLanguage", command.SourceLanguage);
        ValidateOptionalLanguage(errors, "targetLanguage", command.TargetLanguage);

        ThrowIfInvalid(errors);
    }

    public void ValidateUpdate(UpdateConversationCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var errors = new Dictionary<string, string[]>();

        if (command.Title is null
            && command.Instruction is null
            && command.SourceLanguage is null
            && command.TargetLanguage is null)
        {
            errors["request"] = ["Provide at least one field to update."];
        }

        if (command.Title is not null)
        {
            ValidateRequiredText(
                errors,
                "title",
                command.Title,
                Conversation.TitleMaxLength);
        }

        if (command.Instruction is not null)
        {
            ValidateRequiredText(
                errors,
                "instruction",
                command.Instruction,
                Conversation.InstructionMaxLength);
        }

        if (command.SourceLanguage is not null)
        {
            ValidateRequiredLanguage(errors, "sourceLanguage", command.SourceLanguage);
        }

        if (command.TargetLanguage is not null)
        {
            ValidateRequiredLanguage(errors, "targetLanguage", command.TargetLanguage);
        }

        ThrowIfInvalid(errors);
    }

    public void ValidateProcess(ProcessConversationInputCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var errors = new Dictionary<string, string[]>();

        if (!Enum.IsDefined(command.InputKind))
        {
            errors["inputKind"] = ["inputKind must be a supported value."];
        }

        ValidateRequiredText(
            errors,
            "text",
            command.Text,
            Message.TextMaxLength);

        ThrowIfInvalid(errors);
    }

    private static void ValidateOptionalText(
        IDictionary<string, string[]> errors,
        string field,
        string? value,
        int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        AddMaxLengthError(errors, field, value.Trim(), maxLength);
    }

    private static void ValidateRequiredText(
        IDictionary<string, string[]> errors,
        string field,
        string? value,
        int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors[field] = ["Value is required."];
            return;
        }

        AddMaxLengthError(errors, field, value.Trim(), maxLength);
    }

    private static void ValidateOptionalLanguage(
        IDictionary<string, string[]> errors,
        string field,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        ValidateRequiredLanguage(errors, field, value);
    }

    private static void ValidateRequiredLanguage(
        IDictionary<string, string[]> errors,
        string field,
        string value)
    {
        try
        {
            _ = LanguageCode.Normalize(value, field);
        }
        catch (ArgumentException exception)
        {
            errors[field] = [exception.Message];
        }
    }

    private static void AddMaxLengthError(
        IDictionary<string, string[]> errors,
        string field,
        string value,
        int maxLength)
    {
        if (value.Length > maxLength)
        {
            errors[field] = [$"Value cannot exceed {maxLength} characters."];
        }
    }

    private static void ThrowIfInvalid(IReadOnlyDictionary<string, string[]> errors)
    {
        if (errors.Count > 0)
        {
            throw new ValidationException("Validation failed.", errors);
        }
    }
}
