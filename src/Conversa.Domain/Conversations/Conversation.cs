using Conversa.Domain.Common;

namespace Conversa.Domain.Conversations;

public sealed class Conversation
{
    public const int TitleMaxLength = 200;
    public const int InstructionMaxLength = 4000;

    private readonly List<Message> _messages = [];

    private Conversation()
    {
        Title = string.Empty;
        Instruction = string.Empty;
        SourceLanguage = string.Empty;
        TargetLanguage = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string Title { get; private set; }

    public string Instruction { get; private set; }

    public string SourceLanguage { get; private set; }

    public string TargetLanguage { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyCollection<Message> Messages => _messages;

    public static Conversation Create(
        Guid userId,
        string title,
        string instruction,
        string sourceLanguage,
        string targetLanguage,
        DateTimeOffset now)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User id is required.", nameof(userId));
        }

        return new Conversation
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Title = NormalizeTitle(title),
            Instruction = NormalizeInstruction(instruction),
            SourceLanguage = LanguageCode.Normalize(sourceLanguage, nameof(sourceLanguage)),
            TargetLanguage = LanguageCode.Normalize(targetLanguage, nameof(targetLanguage)),
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void Rename(string title, DateTimeOffset now)
    {
        Title = NormalizeTitle(title);
        UpdatedAt = now;
    }

    public void ChangeInstruction(string instruction, DateTimeOffset now)
    {
        Instruction = NormalizeInstruction(instruction);
        UpdatedAt = now;
    }

    public void ChangeLanguages(string sourceLanguage, string targetLanguage, DateTimeOffset now)
    {
        SourceLanguage = LanguageCode.Normalize(sourceLanguage, nameof(sourceLanguage));
        TargetLanguage = LanguageCode.Normalize(targetLanguage, nameof(targetLanguage));
        UpdatedAt = now;
    }

    public void Touch(DateTimeOffset now) => UpdatedAt = now;

    private static string NormalizeTitle(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        var value = title.Trim();
        if (value.Length > TitleMaxLength)
        {
            throw new ArgumentException($"Title cannot exceed {TitleMaxLength} characters.", nameof(title));
        }

        return value;
    }

    private static string NormalizeInstruction(string instruction)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instruction);
        var value = instruction.Trim();
        if (value.Length > InstructionMaxLength)
        {
            throw new ArgumentException(
                $"Instruction cannot exceed {InstructionMaxLength} characters.",
                nameof(instruction));
        }

        return value;
    }
}
