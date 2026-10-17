using System.Diagnostics;
using Conversa.Api.Infrastructure;

namespace Conversa.Api.Tests.Observability;

public sealed class TelemetryPrivacyFilterTests
{
    [Fact]
    public void RedactUrlDetails_RemovesRawUrlAndQueryTagsButKeepsSafeRouteMetadata()
    {
        using var activity = new Activity("conversa.http.privacy").Start();
        var rawUrlTags = new[]
        {
            "url.full",
            "url.path",
            "url.query",
            "http.url",
            "http.target",
            "http.request.uri"
        };

        foreach (var tagName in rawUrlTags)
        {
            activity.SetTag(tagName, "https://example.test/api/conversations/private-id?search=private-text");
        }

        activity.DisplayName = "POST /api/conversations/private-id?search=private-text";
        activity.SetTag("http.route", "/api/conversations/{id}");
        activity.SetTag("http.request.method", "POST");

        new TelemetryPrivacyProcessor().OnEnd(activity);

        foreach (var tagName in rawUrlTags)
        {
            Assert.DoesNotContain(activity.TagObjects, tag => tag.Key == tagName);
        }

        Assert.Equal("POST /api/conversations/{id}", activity.DisplayName);
        Assert.DoesNotContain("private-id", activity.DisplayName, StringComparison.Ordinal);
        Assert.DoesNotContain("private-text", activity.DisplayName, StringComparison.Ordinal);
        Assert.Contains(activity.TagObjects, tag =>
            tag.Key == "http.route" && Equals(tag.Value, "/api/conversations/{id}"));
        Assert.Contains(activity.TagObjects, tag =>
            tag.Key == "http.request.method" && Equals(tag.Value, "POST"));
    }
    [Fact]
    public void RedactUrlDetails_UsesGenericSpanNameWhenNoRouteTemplateExists()
    {
        using var activity = new Activity("http.request").Start();
        activity.DisplayName = "POST /private-id?search=private-text";
        activity.SetTag("http.request.method", "POST");
        activity.SetTag("url.path", "/private-id");
        activity.SetTag("url.query", "?search=private-text");

        new TelemetryPrivacyProcessor().OnEnd(activity);

        Assert.Equal("POST (URL redacted)", activity.DisplayName);
        Assert.DoesNotContain("private-id", activity.DisplayName, StringComparison.Ordinal);
        Assert.DoesNotContain("private-text", activity.DisplayName, StringComparison.Ordinal);
    }

}
