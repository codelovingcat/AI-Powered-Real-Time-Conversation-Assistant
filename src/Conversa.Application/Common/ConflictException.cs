namespace Conversa.Application.Common;

public sealed class ConflictException : Exception
{
    public ConflictException(string message) : base(message)
    {
    }
}
