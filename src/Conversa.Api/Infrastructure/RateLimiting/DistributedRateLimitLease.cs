using System.Threading.RateLimiting;

namespace Conversa.Api.Infrastructure.RateLimiting;

public sealed class DistributedRateLimitLease(
    bool isAcquired,
    TimeSpan? retryAfter = null,
    bool storeAvailable = true) : RateLimitLease
{
    public const string StoreUnavailableReason = "rate_limit_store_unavailable";

    public override bool IsAcquired => isAcquired;

    public override IEnumerable<string> MetadataNames
    {
        get
        {
            if (retryAfter.HasValue)
            {
                yield return MetadataName.RetryAfter.Name;
            }

            if (!storeAvailable)
            {
                yield return MetadataName.ReasonPhrase.Name;
            }
        }
    }

    public override bool TryGetMetadata(string metadataName, out object? metadata)
    {
        if (metadataName == MetadataName.RetryAfter.Name && retryAfter.HasValue)
        {
            metadata = retryAfter.Value;
            return true;
        }

        if (metadataName == MetadataName.ReasonPhrase.Name && !storeAvailable)
        {
            metadata = StoreUnavailableReason;
            return true;
        }

        metadata = null;
        return false;
    }

    protected override void Dispose(bool disposing)
    {
    }
}
