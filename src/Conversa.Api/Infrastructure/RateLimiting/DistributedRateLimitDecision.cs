namespace Conversa.Api.Infrastructure.RateLimiting;

public readonly record struct DistributedRateLimitDecision(
    bool IsAcquired,
    TimeSpan RetryAfter,
    bool StoreAvailable);
