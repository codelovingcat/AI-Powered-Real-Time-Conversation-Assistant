using Conversa.Domain.Conversations;

namespace Conversa.Application.Conversations;

public sealed record ConversationSummaryDto(
    Guid Id,
    Guid UserId,
    string Title,
    string Instruction,
    string SourceLanguage,
    string TargetLanguage,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record MessageDto(
    Guid Id,
    Guid ConversationId,
    MessageRole Role,
    string OriginalText,
    string? Translation,
    string? Explanation,
    string? SuggestedAnswer,
    string? SuggestedAnswerTranslation,
    AiResponseType? ResponseType,
    bool QuestionDetected,
    bool QuestionDirectedAtUser,
    string? ProviderName,
    DateTimeOffset CreatedAt);

public static class ConversationMappings
{
    public static ConversationSummaryDto ToSummary(this Domain.Conversations.Conversation conversation)
        => new(
            conversation.Id,
            conversation.UserId,
            conversation.Title,
            conversation.Instruction,
            conversation.SourceLanguage,
            conversation.TargetLanguage,
            conversation.CreatedAt,
            conversation.UpdatedAt);

    public static MessageDto ToDto(this Message message)
        => new(
            message.Id,
            message.ConversationId,
            message.Role,
            message.OriginalText,
            message.Translation,
            message.Explanation,
            message.SuggestedAnswer,
            message.SuggestedAnswerTranslation,
            message.ResponseType,
            message.QuestionDetected,
            message.QuestionDirectedAtUser,
            message.ProviderName,
            message.CreatedAt);
}
