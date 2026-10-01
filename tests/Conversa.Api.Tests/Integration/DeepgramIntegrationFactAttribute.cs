using Xunit;

namespace Conversa.Api.Tests.Integration;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class DeepgramIntegrationFactAttribute : FactAttribute
{
    public DeepgramIntegrationFactAttribute()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("CONVERSA_RUN_DEEPGRAM_INTEGRATION"),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            Skip = "Set CONVERSA_RUN_DEEPGRAM_INTEGRATION=true to run the real Deepgram integration test.";
        }
    }
}
