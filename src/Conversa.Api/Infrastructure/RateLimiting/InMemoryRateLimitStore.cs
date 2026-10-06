using System.Collections.Concurrent;

namespace Conversa.Api.Infrastructure.RateLimiting;

public sealed class InMemoryRateLimitStore : IDistributedRateLimitStore
{
    private sealed class Entry
    {
        public DateTimeOffset ExpiresAt { get; set; }
        public int Count { get; set; }
    }

    private readonly ConcurrentDictionary<string, Entry> _entries = new();

    public ValueTask<DistributedRateLimitDecision> TryAcquireAsync(
        string key,
        int permitCount,
        int permitLimit,
        TimeSpan window,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var now = DateTimeOffset.UtcNow;
        var entry = _entries.GetOrAdd(
            key,
            _ => new Entry { ExpiresAt = now.Add(window) });

        lock (entry)
        {
            if (now >= entry.ExpiresAt)
            {
                entry.Count = 0;
                entry.ExpiresAt = now.Add(window);
            }

            if (permitCount == 0)
            {
                return ValueTask.FromResult(
                    new DistributedRateLimitDecision(
                        entry.Count < permitLimit,
                        entry.ExpiresAt - now,
                        StoreAvailable: true));
            }

            if (entry.Count + permitCount > permitLimit)
            {
                return ValueTask.FromResult(
                    new DistributedRateLimitDecision(
                        IsAcquired: false,
                        entry.ExpiresAt - now,
                        StoreAvailable: true));
            }

            entry.Count += permitCount;

            return ValueTask.FromResult(
                new DistributedRateLimitDecision(
                    IsAcquired: true,
                    entry.ExpiresAt - now,
                    StoreAvailable: true));
        }
    }
}
