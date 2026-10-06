using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;

namespace Conversa.Api.Infrastructure.RateLimiting;

public sealed class DistributedFixedWindowRateLimiter(
    string policyName,
    string partitionKey,
    IDistributedRateLimitStore store,
    int permitLimit,
    TimeSpan window) : RateLimiter
{
    public override TimeSpan? IdleDuration => null;

    public override RateLimiterStatistics? GetStatistics() => null;

    protected override RateLimitLease AttemptAcquireCore(int permitCount)
        => AcquireAsyncCore(permitCount, CancellationToken.None)
            .AsTask()
            .GetAwaiter()
            .GetResult();

    protected override async ValueTask<RateLimitLease> AcquireAsyncCore(
        int permitCount,
        CancellationToken cancellationToken)
    {
        if (permitCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(permitCount));
        }

        if (permitCount > permitLimit)
        {
            return new DistributedRateLimitLease(
                isAcquired: false,
                retryAfter: window);
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var decision = await store.TryAcquireAsync(
                CreateStorageKey(policyName, partitionKey),
                permitCount,
                permitLimit,
                window,
                cancellationToken);

            return new DistributedRateLimitLease(
                decision.IsAcquired,
                decision.IsAcquired ? null : decision.RetryAfter,
                decision.StoreAvailable);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Fail closed when the shared rate-limit store is unavailable.
            return new DistributedRateLimitLease(
                isAcquired: false,
                retryAfter: TimeSpan.FromSeconds(1),
                storeAvailable: false);
        }
    }

    public static string CreateStorageKey(string policyName, string partitionKey)
    {
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(partitionKey)));

        return $"conversa:ratelimit:v1:{policyName}:{hash}";
    }

    protected override void Dispose(bool disposing)
    {
    }
}
