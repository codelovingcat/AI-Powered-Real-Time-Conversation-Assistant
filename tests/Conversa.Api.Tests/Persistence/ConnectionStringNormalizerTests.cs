using Conversa.Infrastructure.Persistence;
using Xunit;

namespace Conversa.Api.Tests.Persistence;

public sealed class ConnectionStringNormalizerTests
{
    [Fact]
    public void Normalize_removes_one_outer_pair_of_double_quotes()
    {
        const string connectionString =
            "\"Host=localhost;Port=5432;Database=conversa;Username=conversa;Password=conversa;\"";

        var normalized = ConnectionStringNormalizer.Normalize(connectionString);

        Assert.Equal(
            "Host=localhost;Port=5432;Database=conversa;Username=conversa;Password=conversa;",
            normalized);
    }

    [Fact]
    public void Normalize_keeps_regular_npgsql_connection_strings_unchanged()
    {
        const string connectionString =
            "Host=localhost;Port=5432;Database=conversa;Username=conversa;Password=conversa;";

        var normalized = ConnectionStringNormalizer.Normalize(connectionString);

        Assert.Equal(connectionString, normalized);
    }
}
