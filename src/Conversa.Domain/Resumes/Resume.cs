using Conversa.Domain.Common;

namespace Conversa.Domain.Resumes;

public sealed class Resume
{
    public const long MaxFileSizeBytes = 10 * 1024 * 1024;
    public const int FileNameMaxLength = 255;

    private Resume()
    {
        OriginalFileName = string.Empty;
        ContentType = string.Empty;
        StorageKey = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string OriginalFileName { get; private set; }

    public string ContentType { get; private set; }

    public long FileSizeBytes { get; private set; }

    public string StorageKey { get; private set; }

    public string? ExtractedText { get; private set; }

    public ResumeStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Resume Create(
        Guid userId,
        string originalFileName,
        string contentType,
        long fileSizeBytes,
        string storageKey,
        DateTimeOffset now)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User id is required.", nameof(userId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(originalFileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);

        var fileName = originalFileName.Trim();
        if (fileName.Length > FileNameMaxLength)
        {
            throw new ArgumentException(
                $"File name cannot exceed {FileNameMaxLength} characters.",
                nameof(originalFileName));
        }

        if (fileSizeBytes <= 0 || fileSizeBytes > MaxFileSizeBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fileSizeBytes),
                $"File size must be between 1 byte and {MaxFileSizeBytes} bytes.");
        }

        if (!IsSupportedContentType(contentType))
        {
            throw new ArgumentException(
                "Only PDF and DOCX resumes are supported.",
                nameof(contentType));
        }

        return new Resume
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            OriginalFileName = fileName,
            ContentType = contentType.Trim().ToLowerInvariant(),
            FileSizeBytes = fileSizeBytes,
            StorageKey = storageKey.Trim(),
            Status = ResumeStatus.Uploaded,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void MarkParsed(string extractedText, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extractedText);

        ExtractedText = extractedText.Trim();
        Status = ResumeStatus.Parsed;
        UpdatedAt = now;
    }

    public void MarkAnalyzed(DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(ExtractedText))
        {
            throw new InvalidOperationException("Resume must be parsed before analysis.");
        }

        Status = ResumeStatus.Analyzed;
        UpdatedAt = now;
    }

    public void MarkFailed(DateTimeOffset now) 
    {
        Status = ResumeStatus.Failed;
        UpdatedAt = now;
    }

    private static bool IsSupportedContentType(string contentType)
    {
        return contentType.Trim().ToLowerInvariant() is
            "application/pdf" or
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    }
}
