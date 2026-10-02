using Microsoft.AspNetCore.Http;

namespace Conversa.Api.Realtime;

public static class WebSocketAuthentication
{
    public const string SubProtocol = "conversa.auth";

    public static bool TryGetAccessToken(
        IHeaderDictionary headers,
        out string token)
    {
        token = string.Empty;

        var raw = headers["Sec-WebSocket-Protocol"].ToString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var protocols = raw.Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (protocols.Length != 2 ||
            !string.Equals(protocols[0], SubProtocol, StringComparison.Ordinal))
        {
            return false;
        }

        var candidate = protocols[1];

        if (candidate.Length < 32 || candidate.Any(char.IsWhiteSpace))
        {
            return false;
        }

        token = candidate;
        return true;
    }

    public static bool RequestedSubProtocol(IHeaderDictionary headers)
    {
        var raw = headers["Sec-WebSocket-Protocol"].ToString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        return raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(SubProtocol, StringComparer.Ordinal);
    }
}
