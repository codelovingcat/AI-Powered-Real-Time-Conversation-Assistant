namespace Conversa.Api.Infrastructure.RateLimiting;

public interface IDistributedRateLimitStore
{
    ValueTask<DistributedRateLimitDecision> TryAcquireAsync(
        string key,
        int permitCount,
        int permitLimit,
        TimeSpan window,
        CancellationToken cancellationToken);
}
