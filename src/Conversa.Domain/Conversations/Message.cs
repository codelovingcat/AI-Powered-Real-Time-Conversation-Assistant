namespace Conversa.Domain.Conversations;

public sealed class Message
{
    public const int TextMaxLength = 8000;
    public const int ProviderNameMaxLength = 64;

    private Message()
    {
        OriginalText = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid ConversationId { get; private set; }

    public MessageRole Role { get; private set; }

    public string OriginalText { get; private set; }

    public string? Translation { get; private set; }

    public string? Explanation { get; private set; }

    public string? SuggestedAnswer { get; private set; }

    public string? SuggestedAnswerTranslation { get; private set; }

    public AiResponseType? ResponseType { get; private set; }

    public bool QuestionDetected { get; private set; }

    public bool QuestionDirectedAtUser { get; private set; }

    public string? ProviderName { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Message CreateFromAssistant(
        Guid conversationId,
        MessageRole role,
        string originalText,
        string? translation,
        string? explanation,
        string? suggestedAnswer,
        string? suggestedAnswerTranslation,
        AiResponseType responseType,
        bool questionDetected,
        bool questionDirectedAtUser,
        string providerName,
        DateTimeOffset now)
    {
        if (conversationId == Guid.Empty)
        {
            throw new ArgumentException("Conversation id is required.", nameof(conversationId));
        }

        if (role is not (MessageRole.Speaker or MessageRole.User))
        {
            throw new ArgumentException("Processed turns must come from the speaker or the user.", nameof(role));
        }

        return new Message
        {
            Id = Guid.CreateVersion7(),
            ConversationId = conversationId,
            Role = role,
            OriginalText = NormalizeRequired(originalText, nameof(originalText)),
            Translation = NormalizeOptional(translation),
            Explanation = NormalizeOptional(explanation),
            SuggestedAnswer = NormalizeOptional(suggestedAnswer),
            SuggestedAnswerTranslation = NormalizeOptional(suggestedAnswerTranslation),
            ResponseType = responseType,
            QuestionDetected = questionDetected,
            QuestionDirectedAtUser = questionDirectedAtUser,
            ProviderName = NormalizeProvider(providerName),
            CreatedAt = now
        };
    }

    public static Message CreateSystemNote(Guid conversationId, string text, DateTimeOffset now)
    {
        if (conversationId == Guid.Empty)
        {
            throw new ArgumentException("Conversation id is required.", nameof(conversationId));
        }

        return new Message
        {
            Id = Guid.CreateVersion7(),
            ConversationId = conversationId,
            Role = MessageRole.System,
            OriginalText = NormalizeRequired(text, nameof(text)),
            CreatedAt = now
        };
    }

    private static string NormalizeRequired(string value, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var trimmed = value.Trim();
        if (trimmed.Length > TextMaxLength)
        {
            throw new ArgumentException($"Text cannot exceed {TextMaxLength} characters.", paramName);
        }

        return trimmed;
    }

    private static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= TextMaxLength ? trimmed : trimmed[..TextMaxLength];
    }

    private static string NormalizeProvider(string providerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        var trimmed = providerName.Trim();
        if (trimmed.Length > ProviderNameMaxLength)
        {
            throw new ArgumentException("Provider name is too long.", nameof(providerName));
        }

        return trimmed;
    }
}
