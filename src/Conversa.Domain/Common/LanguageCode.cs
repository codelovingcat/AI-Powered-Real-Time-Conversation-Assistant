using System.Text.RegularExpressions;

namespace Conversa.Domain.Common;

public static partial class LanguageCode
{
    public const int MaxLength = 16;

    public static string Normalize(string value, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var trimmed = value.Trim();
        if (trimmed.Length > MaxLength || !TagPattern().IsMatch(trimmed))
        {
            throw new ArgumentException(
                "Language must be a short BCP-47 tag such as \"en\" or \"tr\".",
                paramName);
        }

        return trimmed;
    }

    [GeneratedRegex("^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})*$", RegexOptions.CultureInvariant)]
    private static partial Regex TagPattern();
}
