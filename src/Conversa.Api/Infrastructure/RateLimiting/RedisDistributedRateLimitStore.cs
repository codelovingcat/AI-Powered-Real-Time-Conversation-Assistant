using StackExchange.Redis;

namespace Conversa.Api.Infrastructure.RateLimiting;

public sealed class RedisDistributedRateLimitStore(
    IConnectionMultiplexer connectionMultiplexer) : IDistributedRateLimitStore
{
    private const string IncrementScript = """
local current = tonumber(redis.call('GET', KEYS[1]) or '0')
local requested = tonumber(ARGV[1])
local limit = tonumber(ARGV[3])

if requested = 0 then
    local ttl = redis.call('PTTL', KEYS[1])
    if current >= limit then
        return {0, ttl}
    end
    return {1, ttl}
end

if current + requested > limit then
    local ttl = redis.call('PTTL', KEYS[1])
    return {0, ttl}
end

local newCount = redis.call('INCRBY', KEYS[1], requested)
local ttl = redis.call('PTTL', KEYS[1])

if ttl < 0 then
    redis.call('PEXPIRE', KEYS[1], ARGV[2])
    ttl = tonumber(ARGV[2])
end

return {1, ttl}
""";

    public async ValueTask<DistributedRateLimitDecision> TryAcquireAsync(
        string key,
        int permitCount,
        int permitLimit,
        TimeSpan window,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var database = connectionMultiplexer.GetDatabase();
        var result = await database.ScriptEvaluateAsync(
            IncrementScript,
            [new RedisKey(key)],
            [
                (RedisValue)permitCount,
                (RedisValue)Math.Max(1, (long)window.TotalMilliseconds),
                (RedisValue)permitLimit
            ]);

        cancellationToken.ThrowIfCancellationRequested();

        var values = (RedisResult[])result;
        var isAcquired = (long)values[0] == 1;
        var retryAfterMilliseconds = (long)values[1];

        var retryAfter = TimeSpan.FromMilliseconds(
            Math.Max(1, retryAfterMilliseconds));

        return new DistributedRateLimitDecision(
            isAcquired,
            retryAfter,
            StoreAvailable: true);
    }
}
