using System.Diagnostics;

namespace Conversa.Api.Infrastructure;

/// <summary>
/// Removes raw URL values from HTTP spans so paths, identifiers and query values are not exported.
/// </summary>
public static class TelemetryPrivacyFilter
{
    private static readonly string[] RawUrlTagNames =
    [
        "url.full",
        "url.path",
        "url.query",
        "http.url",
        "http.target",
        "http.request.uri"
    ];

    public static void RedactUrlDetails(Activity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);

        var containsRawUrl = RawUrlTagNames.Any(tagName => activity.GetTagItem(tagName) is not null);
        var method = activity.GetTagItem("http.request.method") as string
            ?? activity.GetTagItem("http.method") as string;
        var route = activity.GetTagItem("http.route") as string;

        foreach (var tagName in RawUrlTagNames)
        {
            activity.SetTag(tagName, null);
        }

        // Span names are exported too. When a raw URL was present, retain only a stable route template
        // or a generic method label so a fallback name cannot leak an identifier or query value.
        if (containsRawUrl)
        {
            var safeMethod = string.IsNullOrWhiteSpace(method) ? "HTTP" : method;
            activity.DisplayName = string.IsNullOrWhiteSpace(route)
                ? $"{safeMethod} (URL redacted)"
                : $"{safeMethod} {route}";
        }
    }
}
