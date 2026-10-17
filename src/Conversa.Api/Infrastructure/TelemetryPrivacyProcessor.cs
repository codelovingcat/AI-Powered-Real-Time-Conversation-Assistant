using System.Diagnostics;
using OpenTelemetry;

namespace Conversa.Api.Infrastructure;

/// <summary>
/// Removes raw HTTP URL values after instrumentation has populated span tags and before export.
/// </summary>
public sealed class TelemetryPrivacyProcessor : BaseProcessor<Activity>
{
    public override void OnEnd(Activity data)
    {
        TelemetryPrivacyFilter.RedactUrlDetails(data);
    }
}
