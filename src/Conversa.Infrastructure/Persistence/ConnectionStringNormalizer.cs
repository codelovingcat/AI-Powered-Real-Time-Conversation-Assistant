namespace Conversa.Infrastructure.Persistence;

public static class ConnectionStringNormalizer
{
    public static string Normalize(string connectionString)
    {
        ArgumentNullException.ThrowIfNull(connectionString);

        var value = connectionString.Trim();

        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            return value[1..^1].Trim();
        }

        return value;
    }
}
