namespace Conversa.Application.Common;

public sealed class NotFoundException : Exception
{
    public NotFoundException(string message)
        : base(message)
    {
    }
}

public sealed class ValidationException : Exception
{
    public ValidationException(string message, IReadOnlyDictionary<string, string[]> errors)
        : base(message)
    {
        Errors = errors;
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public static ValidationException For(string field, string message)
        => new("Validation failed.", new Dictionary<string, string[]> { [field] = [message] });
}

public sealed class AiProviderNotConfiguredException : Exception
{
    public AiProviderNotConfiguredException(string message)
        : base(message)
    {
    }
}

public sealed class AiProviderException : Exception
{
    public AiProviderException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public sealed class SpeechToTextNotConfiguredException : Exception
{
    public SpeechToTextNotConfiguredException()
        : base("No speech-to-text provider is registered.")
    {
    }
}
