using Xunit;

namespace Conversa.Api.Tests.Integration;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class GeminiIntegrationFactAttribute : FactAttribute
{
    public GeminiIntegrationFactAttribute()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("CONVERSA_RUN_GEMINI_INTEGRATION"),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            Skip = "Set CONVERSA_RUN_GEMINI_INTEGRATION=true to run the real Gemini API smoke test.";
        }
    }
}
