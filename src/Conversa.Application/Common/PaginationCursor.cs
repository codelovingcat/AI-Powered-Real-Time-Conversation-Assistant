using System.Text;
using System.Text.Json;

namespace Conversa.Application.Common;

public static class PaginationCursor
{
    private const int MaxCursorLength = 512;

    public static string Encode(DateTimeOffset timestamp, Guid id)
    {
        var payload = JsonSerializer.Serialize(new CursorPayload(timestamp, id));
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(payload))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static (DateTimeOffset Timestamp, Guid Id) Decode(
        string value,
        string parameterName = "cursor")
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxCursorLength)
            throw ValidationException.For(parameterName, "Cursor is invalid.");

        try
        {
            var normalized = value.Replace('-', '+').Replace('_', '/');
            normalized += (normalized.Length % 4) switch
            {
                0 => string.Empty,
                2 => "==",
                3 => "=",
                _ => throw new FormatException()
            };

            var json = Encoding.UTF8.GetString(Convert.FromBase64String(normalized));
            var payload = JsonSerializer.Deserialize<CursorPayload>(json);

            if (payload is null || payload.Id == Guid.Empty)
                throw new FormatException();

            return (payload.Timestamp, payload.Id);
        }
        catch (Exception exception) when (
            exception is FormatException
            or JsonException
            or NotSupportedException)
        {
            throw ValidationException.For(parameterName, "Cursor is invalid.");
        }
    }

    private sealed record CursorPayload(DateTimeOffset Timestamp, Guid Id);
}
