using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace Conversa.Api.Infrastructure.RateLimiting;

public sealed class RedisRateLimitHealthCheck(
    IConnectionMultiplexer connectionMultiplexer) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await connectionMultiplexer
                .GetDatabase()
                .PingAsync();

            return HealthCheckResult.Healthy();
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy(
                "Distributed rate-limit store is unavailable.",
                exception);
        }
    }
}
