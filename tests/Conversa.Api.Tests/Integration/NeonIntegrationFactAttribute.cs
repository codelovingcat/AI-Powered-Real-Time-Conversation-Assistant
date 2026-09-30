using Xunit;

namespace Conversa.Api.Tests.Integration;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class NeonIntegrationFactAttribute : FactAttribute
{
    public NeonIntegrationFactAttribute()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("CONVERSA_RUN_NEON_INTEGRATION"),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            Skip = "Set CONVERSA_RUN_NEON_INTEGRATION=true to run the real Neon PostgreSQL integration test.";
        }
    }
}
