using Microsoft.AspNetCore.Http;
using Conversa.Api.Realtime;

namespace Conversa.Api.Tests.Security;

public sealed class WebSocketAuthenticationTests
{
    [Fact]
    public void TryGetAccessToken_accepts_only_the_expected_two_protocols()
    {
        var headers = new HeaderDictionary
        {
            ["Sec-WebSocket-Protocol"] =
                $"{WebSocketAuthentication.SubProtocol}, eyJhbGciOiJIUzI1NiJ9.test.signature"
        };

        var accepted = WebSocketAuthentication.TryGetAccessToken(
            headers,
            out var token);

        Assert.True(accepted);
        Assert.Equal("eyJhbGciOiJIUzI1NiJ9.test.signature", token);
    }

    [Fact]
    public void TryGetAccessToken_rejects_extra_protocols()
    {
        var headers = new HeaderDictionary
        {
            ["Sec-WebSocket-Protocol"] =
                $"{WebSocketAuthentication.SubProtocol}, token, unexpected"
        };

        Assert.False(
            WebSocketAuthentication.TryGetAccessToken(headers, out _));
    }

    [Fact]
    public void TryGetAccessToken_rejects_missing_or_short_tokens()
    {
        var missing = new HeaderDictionary
        {
            ["Sec-WebSocket-Protocol"] = WebSocketAuthentication.SubProtocol
        };
        var shortToken = new HeaderDictionary
        {
            ["Sec-WebSocket-Protocol"] =
                $"{WebSocketAuthentication.SubProtocol}, too-short"
        };

        Assert.False(
            WebSocketAuthentication.TryGetAccessToken(missing, out _));
        Assert.False(
            WebSocketAuthentication.TryGetAccessToken(shortToken, out _));
    }

    [Fact]
    public void RequestedSubProtocol_detects_browser_auth_protocol()
    {
        var headers = new HeaderDictionary
        {
            ["Sec-WebSocket-Protocol"] =
                $"{WebSocketAuthentication.SubProtocol}, token-with-valid-length-123456"
        };

        Assert.True(
            WebSocketAuthentication.RequestedSubProtocol(headers));
    }
}
