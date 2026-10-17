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

        foreach (var tagName in RawUrlTagNames)
        {
            activity.SetTag(tagName, null);
        }
    }
}
