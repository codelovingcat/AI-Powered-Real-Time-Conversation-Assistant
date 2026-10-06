using System.Net;
using System.Security.Claims;
using Conversa.Api.Infrastructure.RateLimiting;
using Microsoft.AspNetCore.Http;
using StackExchange.Redis;
using Xunit;

namespace Conversa.Api.Tests.Security;

public sealed class DistributedRateLimitingTests
{
    [Fact]
    public async Task Shared_store_enforces_one_limit_across_two_limiter_instances_under_concurrency()
    {
        var store = new InMemoryRateLimitStore();
        using var first = CreateLimiter("user:shared", store, permitLimit: 5);
        using var second = CreateLimiter("user:shared", store, permitLimit: 5);

        var leases = await Task.WhenAll(
            Enumerable.Range(0, 20)
                .Select(index =>
                    (index % 2 == 0 ? first : second)
                        .AcquireAsync(1)
                        .AsTask()));

        Assert.Equal(5, leases.Count(lease => lease.IsAcquired));
        Assert.Equal(15, leases.Count(lease => !lease.IsAcquired));

        foreach (var lease in leases)
        {
            lease.Dispose();
        }
    }

    [Fact]
    public async Task Rejected_lease_exposes_retry_after()
    {
        var store = new InMemoryRateLimitStore();
        using var limiter = CreateLimiter("user:retry", store, permitLimit: 1);

        using var first = await limiter.AcquireAsync(1);
        using var rejected = await limiter.AcquireAsync(1);

        Assert.True(first.IsAcquired);
        Assert.False(rejected.IsAcquired);
        Assert.True(
            rejected.TryGetMetadata(
                System.Threading.RateLimiting.MetadataName.RetryAfter,
                out TimeSpan retryAfter));
        Assert.True(retryAfter > TimeSpan.Zero);
    }

    [Fact]
    public async Task Store_failure_fails_closed_without_exposing_backend_details()
    {
        var store = new ThrowingRateLimitStore();
        using var limiter = CreateLimiter("user:failure", store, permitLimit: 1);

        using var rejected = await limiter.AcquireAsync(1);

        Assert.False(rejected.IsAcquired);
        Assert.True(
            rejected.TryGetMetadata(
                System.Threading.RateLimiting.MetadataName.ReasonPhrase,
                out string? reason));
        Assert.Equal(
            DistributedRateLimitLease.StoreUnavailableReason,
            reason);
        Assert.DoesNotContain("Redis", reason, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            rejected.TryGetMetadata(
                System.Threading.RateLimiting.MetadataName.RetryAfter,
                out TimeSpan retryAfter));
        Assert.Equal(TimeSpan.FromSeconds(1), retryAfter);
    }

    [Fact]
    public void Authenticated_partition_uses_server_derived_claim_and_ignores_client_header()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.10");
        context.Request.Headers["X-User-Id"] = "attacker-controlled-id";
        context.User = new ClaimsPrincipal(
            new ClaimsIdentity(
                [new Claim(
                    ClaimTypes.NameIdentifier,
                    "user-from-validated-jwt")],
                "Bearer"));

        var partitionKey = RateLimitPartitionKey.Create(context);

        Assert.Equal("user:user-from-validated-jwt", partitionKey);
        Assert.DoesNotContain("attacker-controlled-id", partitionKey);
    }

    [Fact]
    public void Storage_key_hashes_partition_identity()
    {
        const string partitionKey = "user:sensitive-user-identifier";
        var storageKey =
            DistributedFixedWindowRateLimiter.CreateStorageKey("ai", partitionKey);

        Assert.StartsWith("conversa:ratelimit:v1:ai:", storageKey);
        Assert.DoesNotContain(partitionKey, storageKey, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Redis_store_is_atomic_across_two_connections()
    {
        if (!string.Equals(
            Environment.GetEnvironmentVariable("CONVERSA_RUN_REDIS_INTEGRATION"),
            "true",
            StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var connectionString =
            Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING")
            ?? "redis://localhost:6379";

        using var firstConnection =
            await ConnectionMultiplexer.ConnectAsync(
                RedisConnectionOptionsFactory.Create(connectionString));
        using var secondConnection =
            await ConnectionMultiplexer.ConnectAsync(
                RedisConnectionOptionsFactory.Create(connectionString));

        var firstStore = new RedisDistributedRateLimitStore(firstConnection);
        var secondStore = new RedisDistributedRateLimitStore(secondConnection);

        var partitionKey = $"integration:{Guid.NewGuid():N}";
        const int permitLimit = 8;

        using var first =
            CreateLimiter(partitionKey, firstStore, permitLimit);
        using var second =
            CreateLimiter(partitionKey, secondStore, permitLimit);

        var leases = await Task.WhenAll(
            Enumerable.Range(0, 32)
                .Select(index =>
                    (index % 2 == 0 ? first : second)
                        .AcquireAsync(1)
                        .AsTask()));

        Assert.Equal(
            permitLimit,
            leases.Count(lease => lease.IsAcquired));

        var key =
            DistributedFixedWindowRateLimiter.CreateStorageKey(
                "integration",
                partitionKey);

        await firstConnection.GetDatabase().KeyDeleteAsync(key);

        foreach (var lease in leases)
        {
            lease.Dispose();
        }
    }

    private static DistributedFixedWindowRateLimiter CreateLimiter(
        string partitionKey,
        IDistributedRateLimitStore store,
        int permitLimit)
        => new(
            "test",
            partitionKey,
            store,
            permitLimit,
            TimeSpan.FromSeconds(30));

    private sealed class ThrowingRateLimitStore : IDistributedRateLimitStore
    {
        public ValueTask<DistributedRateLimitDecision> TryAcquireAsync(
            string key,
            int permitCount,
            int permitLimit,
            TimeSpan window,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException(
                "backend failure details must stay server-side");
    }
}
