using System.Diagnostics;
using Conversa.Application.Observability;

namespace Conversa.Api.Tests.Observability;

public sealed class ConversaTelemetryTests
{
    [Fact]
    public void Assistant_telemetry_is_correlated_and_contains_no_user_content()
    {
        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ConversaTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllData,
            ActivityStopped = activity => activities.Add(activity)
        };
        ConversaTelemetry.ActivitySource.AddActivityListener(listener);

        using (ConversaTelemetry.ActivitySource.StartActivity("conversa.test"))
        {
        }

        var activity = Assert.Single(activities);
        Assert.Equal("conversa.test", activity.OperationName);
    }

    [Fact]
    public void Telemetry_tags_are_limited_to_low_cardinality_metadata()
    {
        var tags = ConversaTelemetry.Tags("gemini", "process", "success");

        Assert.Equal(3, tags.Count);
        Assert.Contains(tags, tag => tag.Key == "provider" && Equals(tag.Value, "gemini"));
        Assert.Contains(tags, tag => tag.Key == "operation" && Equals(tag.Value, "process"));
        Assert.Contains(tags, tag => tag.Key == "outcome" && Equals(tag.Value, "success"));
    }
}
