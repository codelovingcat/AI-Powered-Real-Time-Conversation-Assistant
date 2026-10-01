using Conversa.Domain.Conversations;
using Conversa.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Data.Common;

namespace Conversa.Api.Tests.Integration;

public sealed class NeonDatabaseIntegrationTests
{
    [NeonIntegrationFact]
    public async Task Neon_database_supports_migrations_and_conversation_message_crud()
    {
        var pooledConnectionString = Environment.GetEnvironmentVariable("NEON_DATABASE_URL");
        var migrationConnectionString = Environment.GetEnvironmentVariable("NEON_DATABASE_URL_UNPOOLED");

        Assert.False(
            string.IsNullOrWhiteSpace(pooledConnectionString),
            "NEON_DATABASE_URL must be provided by the GitHub Actions secret.");

        Assert.False(
            string.IsNullOrWhiteSpace(migrationConnectionString),
            "NEON_DATABASE_URL_UNPOOLED must be provided by the GitHub Actions secret.");

        try
        {
            _ = new NpgsqlConnectionStringBuilder(pooledConnectionString!);
        }
        catch (ArgumentException)
        {
            var format = DescribeConnectionStringFormat(pooledConnectionString!);
            var unsupportedKeys = FindUnsupportedConnectionStringKeys(pooledConnectionString!);

            Assert.Fail(
                unsupportedKeys.Count > 0
                    ? $"The pooled Neon connection string could not be parsed by Npgsql. Detected format: {format}. Unsupported parameter(s): {string.Join(", ", unsupportedKeys)}."
                    : $"The pooled Neon connection string could not be parsed by Npgsql. Detected format: {format}. No unsupported parameter name was isolated; check the connection-string syntax and quoting.");
        }

        await using (var pooledDb = CreateDbContext(pooledConnectionString!))
        {
            try
            {
                await pooledDb.Database.OpenConnectionAsync();
                await pooledDb.Database.CloseConnectionAsync();
            }
            catch (DbException exception)
            {
                Assert.Fail(
                    $"The pooled Neon PostgreSQL connection failed with database error code {exception.ErrorCode}.");
            }
            catch (Exception exception)
            {
                Assert.Fail(
                    $"The pooled Neon PostgreSQL connection failed with {exception.GetType().Name}.");
            }
        }

        await using var db = CreateDbContext(migrationConnectionString!);

        await db.Database.MigrateAsync();

        var userId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await using var transaction = await db.Database.BeginTransactionAsync();

        var conversation = Conversation.Create(
            userId,
            "Neon integration test",
            "Translate English into natural Turkish.",
            "en",
            "tr",
            now);

        await db.Conversations.AddAsync(conversation);

        var message = Message.CreateFromAssistant(
            conversation.Id,
            MessageRole.Speaker,
            "Hello from Neon.",
            "Neon'dan merhaba.",
            null,
            null,
            null,
            AiResponseType.Translation,
            false,
            false,
            "neon-integration-test",
            now);

        await db.Messages.AddAsync(message);
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();

        var storedConversation = await db.Conversations
            .SingleAsync(item => item.Id == conversation.Id);

        var storedMessage = await db.Messages
            .SingleAsync(item => item.Id == message.Id);

        Assert.Equal("Neon integration test", storedConversation.Title);
        Assert.Equal("Neon'dan merhaba.", storedMessage.Translation);

        storedConversation.Rename("Neon integration test updated", now.AddMinutes(1));
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();

        var updatedConversation = await db.Conversations
            .SingleAsync(item => item.Id == conversation.Id);

        Assert.Equal("Neon integration test updated", updatedConversation.Title);

        db.Conversations.Remove(updatedConversation);
        await db.SaveChangesAsync();

        Assert.False(
            await db.Conversations.AnyAsync(item => item.Id == conversation.Id),
            "The conversation must be deleted.");

        Assert.False(
            await db.Messages.AnyAsync(item => item.Id == message.Id),
            "Deleting the conversation must cascade to its message.");

        await transaction.RollbackAsync();
    }

    private static string DescribeConnectionStringFormat(string connectionString)
    {
        var value = connectionString.Trim();

        if (value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase))
        {
            return "PostgreSQL URI";
        }

        if (value.Contains('=') && value.Contains(';'))
        {
            return "Npgsql key-value";
        }

        return "unknown";
    }

    private static IReadOnlyList<string> FindUnsupportedConnectionStringKeys(string connectionString)
    {
        var unsupportedKeys = new List<string>();

        foreach (var segment in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = segment.IndexOf('=');

            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = segment[..separatorIndex].Trim();

            try
            {
                var builder = new NpgsqlConnectionStringBuilder();
                builder[key] = "placeholder";
            }
            catch (ArgumentException)
            {
                unsupportedKeys.Add(key);
            }
        }

        return unsupportedKeys
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static AppDbContext CreateDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .Options;

        return new AppDbContext(options);
    }
}
