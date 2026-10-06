using System.Net;
using StackExchange.Redis;

namespace Conversa.Api.Infrastructure.RateLimiting;

public static class RedisConnectionOptionsFactory
{
    public static ConfigurationOptions Create(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        if (Uri.TryCreate(connectionString, UriKind.Absolute, out var uri)
            && (uri.Scheme.Equals("redis", StringComparison.OrdinalIgnoreCase)
                || uri.Scheme.Equals("rediss", StringComparison.OrdinalIgnoreCase)))
        {
            var options = new ConfigurationOptions
            {
                AbortOnConnectFail = false,
                Ssl = uri.Scheme.Equals("rediss", StringComparison.OrdinalIgnoreCase),
                ConnectTimeout = 3000,
                AsyncTimeout = 2000,
                SyncTimeout = 2000
            };

            var port = uri.Port > 0
                ? uri.Port
                : options.Ssl ? 6380 : 6379;

            options.EndPoints.Add(new DnsEndPoint(uri.Host, port));

            if (!string.IsNullOrWhiteSpace(uri.UserInfo))
            {
                var separator = uri.UserInfo.IndexOf(':');
                if (separator < 0)
                {
                    options.User = Uri.UnescapeDataString(uri.UserInfo);
                }
                else
                {
                    options.User = Uri.UnescapeDataString(uri.UserInfo[..separator]);
                    options.Password = Uri.UnescapeDataString(uri.UserInfo[(separator + 1)..]);
                }
            }

            return options;
        }

        var parsed = ConfigurationOptions.Parse(connectionString);
        parsed.AbortOnConnectFail = false;
        parsed.ConnectTimeout = 3000;
        parsed.AsyncTimeout = 2000;
        parsed.SyncTimeout = 2000;
        return parsed;
    }
}
