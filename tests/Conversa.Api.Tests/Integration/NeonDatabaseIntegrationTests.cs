using System.Collections.Concurrent;
using System.Diagnostics;
using Conversa.Domain.Conversations;
using Conversa.Infrastructure.Persistence;
using Conversa.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Data.Common;
using Xunit.Abstractions;

namespace Conversa.Api.Tests.Integration;

public sealed class NeonDatabaseIntegrationTests(ITestOutputHelper output)
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

        pooledConnectionString = ConnectionStringNormalizer.Normalize(pooledConnectionString!);
        migrationConnectionString = ConnectionStringNormalizer.Normalize(migrationConnectionString!);

        try
        {
            _ = new NpgsqlConnectionStringBuilder(pooledConnectionString!);
        }
        catch (ArgumentException)
        {
            Assert.Fail(
                $"The pooled Neon connection string could not be parsed by Npgsql. Detected format: {DescribeConnectionStringFormat(pooledConnectionString!)}.");
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
        var conversationSummary = ConversationContextSummary.Create(
            conversation.Id,
            "Synthetic summary for cascade deletion",
            message.CreatedAt,
            message.Id,
            now,
            "neon-integration-test");
        await db.ConversationContextSummaries.AddAsync(conversationSummary);
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();

        var storedConversation = await db.Conversations
            .SingleAsync(item => item.Id == conversation.Id);

        var storedMessage = await db.Messages
            .SingleAsync(item => item.Id == message.Id);

        Assert.Equal("Neon integration test", storedConversation.Title);
        Assert.Equal("Neon'dan merhaba.", storedMessage.Translation);
        Assert.Equal(
            "Synthetic summary for cascade deletion",
            (await db.ConversationContextSummaries.SingleAsync(item => item.ConversationId == conversation.Id)).Summary);

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
        Assert.False(
            await db.ConversationContextSummaries.AnyAsync(item => item.ConversationId == conversation.Id),
            "Deleting the conversation must cascade to its generated context summary.");

        await transaction.RollbackAsync();
    }

    [NeonIntegrationFact]
    public async Task Concurrent_synthetic_conversation_writes_complete_and_cleanup()
    {
        const int concurrentWrites = 16;
        var pooledConnectionString = Environment.GetEnvironmentVariable("NEON_DATABASE_URL");
        var migrationConnectionString = Environment.GetEnvironmentVariable("NEON_DATABASE_URL_UNPOOLED");

        Assert.False(string.IsNullOrWhiteSpace(pooledConnectionString));
        Assert.False(string.IsNullOrWhiteSpace(migrationConnectionString));

        pooledConnectionString = ConnectionStringNormalizer.Normalize(pooledConnectionString!);
        migrationConnectionString = ConnectionStringNormalizer.Normalize(migrationConnectionString!);

        var runId = Guid.NewGuid().ToString("N");
        var userId = Guid.NewGuid();
        var titlePrefix = $"Synthetic Neon load {runId} ";
        var now = DateTimeOffset.UtcNow;
        var conversations = Enumerable.Range(0, concurrentWrites)
            .Select(index => Conversation.Create(
                userId,
                $"{titlePrefix}{index:D2}",
                "Synthetic concurrent persistence probe.",
                "en",
                "tr",
                now))
            .ToArray();

        var latencies = new ConcurrentBag<double>();
        var failures = 0;
        var completedWrites = 0;
        var verifiedMessages = 0;
        var schemaReady = false;
        var totalWatch = Stopwatch.StartNew();

        try
        {
            await using (var migrationDb = CreateDbContext(migrationConnectionString!))
            {
                await migrationDb.Database.MigrateAsync();
            }

            schemaReady = true;

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            var startGate = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            var tasks = conversations.Select(async (conversation, index) =>
            {
                await startGate.Task.WaitAsync(timeout.Token);
                try
                {
                    await using var worker = CreateDbContext(pooledConnectionString!);
                    var message = Message.CreateFromAssistant(
                        conversation.Id,
                        MessageRole.Speaker,
                        $"Synthetic concurrent input {index}",
                        $"Synthetic concurrent translation {index}",
                        null,
                        null,
                        null,
                        AiResponseType.Translation,
                        false,
                        false,
                        "neon-load-test",
                        now);

                    var watch = Stopwatch.StartNew();
                    await worker.Conversations.AddAsync(conversation, timeout.Token);
                    await worker.Messages.AddAsync(message, timeout.Token);
                    await worker.SaveChangesAsync(timeout.Token);
                    watch.Stop();

                    latencies.Add(watch.Elapsed.TotalMilliseconds);
                    Interlocked.Increment(ref completedWrites);
                }
                catch
                {
                    Interlocked.Increment(ref failures);
                    throw;
                }
            }).ToArray();

            startGate.TrySetResult(true);
            var allWrites = Task.WhenAll(tasks);
            try
            {
                await allWrites.WaitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    await allWrites;
                }
                catch
                {
                    // Preserve the timeout/cancellation signal after all workers settle for cleanup.
                }

                throw;
            }

            await using var verificationDb = CreateDbContext(pooledConnectionString!);
            var storedConversations = await verificationDb.Conversations
                .Where(conversation =>
                    conversation.UserId == userId &&
                    conversation.Title.StartsWith(titlePrefix))
                .ToArrayAsync(timeout.Token);
            Assert.Equal(concurrentWrites, storedConversations.Length);

            var storedIds = storedConversations.Select(conversation => conversation.Id).ToArray();
            verifiedMessages = await verificationDb.Messages
                .CountAsync(message => storedIds.Contains(message.ConversationId), timeout.Token);
            Assert.Equal(concurrentWrites, verifiedMessages);
        }
        finally
        {
            if (schemaReady)
            {
                await using var cleanupDb = CreateDbContext(pooledConnectionString!);
                var syntheticConversations = await cleanupDb.Conversations
                    .Where(conversation =>
                        conversation.UserId == userId &&
                        conversation.Title.StartsWith(titlePrefix))
                    .ToArrayAsync();
                var syntheticIds = syntheticConversations
                    .Select(conversation => conversation.Id)
                    .ToArray();

                if (syntheticIds.Length > 0)
                {
                    var syntheticMessages = await cleanupDb.Messages
                        .Where(message => syntheticIds.Contains(message.ConversationId))
                        .ToArrayAsync();
                    cleanupDb.Messages.RemoveRange(syntheticMessages);
                    cleanupDb.Conversations.RemoveRange(syntheticConversations);
                    await cleanupDb.SaveChangesAsync();
                }

                cleanupDb.ChangeTracker.Clear();
                Assert.Equal(
                    0,
                    await cleanupDb.Conversations.CountAsync(conversation =>
                        conversation.UserId == userId &&
                        conversation.Title.StartsWith(titlePrefix)));
                if (syntheticIds.Length > 0)
                {
                    Assert.Equal(
                        0,
                        await cleanupDb.Messages.CountAsync(message =>
                            syntheticIds.Contains(message.ConversationId)));
                }
            }
        }

        totalWatch.Stop();
        var values = latencies.Order().ToArray();
        var p50 = Percentile(values, 0.50);
        var p95 = Percentile(values, 0.95);

        output.WriteLine(
            "scenario=neon-postgres-concurrency writes={0} total_ms={1:F1} p50_write_ms={2:F1} p95_write_ms={3:F1} completed={4} verified_messages={5} failures={6} synthetic_rows_cleaned=true",
            concurrentWrites,
            totalWatch.Elapsed.TotalMilliseconds,
            p50,
            p95,
            completedWrites,
            verifiedMessages,
            failures);

        Assert.Equal(concurrentWrites, completedWrites);
        Assert.Equal(0, failures);
        Assert.True(
            totalWatch.Elapsed < TimeSpan.FromSeconds(20),
            $"The {concurrentWrites}-write synthetic Neon probe exceeded 20 seconds including cleanup.");
    }

    [NeonIntegrationFact]
    public async Task Conversation_summaries_are_owner_scoped_and_revisions_are_optimistic()
    {
        var migrationConnectionString = Environment.GetEnvironmentVariable("NEON_DATABASE_URL_UNPOOLED");
        Assert.False(string.IsNullOrWhiteSpace(migrationConnectionString));
        migrationConnectionString = ConnectionStringNormalizer.Normalize(migrationConnectionString!);

        await using var db = CreateDbContext(migrationConnectionString!);
        await db.Database.MigrateAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();

        var ownerA = Guid.NewGuid();
        var ownerB = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var conversationA = Conversation.Create(ownerA, $"summary-owner-a-{Guid.NewGuid():N}", "Current instruction A", "en", "tr", now);
        var conversationB = Conversation.Create(ownerB, $"summary-owner-b-{Guid.NewGuid():N}", "Current instruction B", "en", "tr", now);
        await db.Conversations.AddRangeAsync(conversationA, conversationB);

        var messages = Enumerable.Range(0, 3)
            .Select(index => Message.CreateFromAssistant(
                conversationA.Id,
                MessageRole.Speaker,
                $"synthetic summary cursor {index}",
                $"synthetic translation {index}",
                null,
                null,
                null,
                AiResponseType.Translation,
                false,
                false,
                "summary-ownership-test",
                now.AddMinutes(-3 + index),
                "conversa-gemini-test-v1"))
            .ToArray();
        await db.Messages.AddRangeAsync(messages);
        await db.SaveChangesAsync();

        Assert.All(
            await db.Messages.AsNoTracking()
                .Where(message => message.ConversationId == conversationA.Id)
                .Select(message => message.PromptVersion)
                .ToArrayAsync(),
            version => Assert.Equal("conversa-gemini-test-v1", version));

        var repository = new ConversationContextSummaryRepository(db);
        Assert.Null(await repository.GetByConversationIdAsync(conversationA.Id, ownerB, CancellationToken.None));
        Assert.True(await repository.TrySaveAsync(
            conversationA.Id,
            ownerA,
            expectedRevision: 0,
            summary: "Synthetic summary revision one",
            throughCreatedAt: messages[0].CreatedAt,
            throughMessageId: messages[0].Id,
            updatedAt: now,
            cancellationToken: CancellationToken.None,
            promptVersion: "conversa-summary-test-v1"));

        var ownerScoped = await repository.GetByConversationIdAsync(conversationA.Id, ownerA, CancellationToken.None);
        Assert.NotNull(ownerScoped);
        Assert.Equal(1, ownerScoped.Revision);
        Assert.Equal("Synthetic summary revision one", ownerScoped.Summary);
        Assert.Null(await repository.GetByConversationIdAsync(conversationA.Id, ownerB, CancellationToken.None));
        Assert.Null(await repository.GetByConversationIdAsync(conversationB.Id, ownerA, CancellationToken.None));

        var pending = await repository.ListUnsummarizedMessagesAsync(
            conversationA.Id,
            ownerA,
            messages[0].CreatedAt,
            messages[0].Id,
            messages[2].CreatedAt,
            messages[2].Id,
            take: 8,
            CancellationToken.None);
        Assert.Collection(pending, message => Assert.Equal(messages[1].Id, message.Id));
        Assert.Empty(await repository.ListUnsummarizedMessagesAsync(
            conversationA.Id,
            ownerB,
            messages[0].CreatedAt,
            messages[0].Id,
            messages[2].CreatedAt,
            messages[2].Id,
            take: 8,
            CancellationToken.None));

        Assert.True(await repository.TrySaveAsync(
            conversationA.Id,
            ownerA,
            expectedRevision: 1,
            summary: "Synthetic summary revision two",
            throughCreatedAt: messages[1].CreatedAt,
            throughMessageId: messages[1].Id,
            updatedAt: now.AddMinutes(1),
            cancellationToken: CancellationToken.None,
            promptVersion: "conversa-summary-test-v1"));
        Assert.False(await repository.TrySaveAsync(
            conversationA.Id,
            ownerA,
            expectedRevision: 1,
            summary: "Stale writer must not replace the newer summary",
            throughCreatedAt: messages[2].CreatedAt,
            throughMessageId: messages[2].Id,
            updatedAt: now.AddMinutes(2),
            cancellationToken: CancellationToken.None));

        var latest = await repository.GetByConversationIdAsync(conversationA.Id, ownerA, CancellationToken.None);
        Assert.NotNull(latest);
        Assert.Equal(2, latest.Revision);
        Assert.Equal("Synthetic summary revision two", latest.Summary);
        Assert.Equal("conversa-summary-test-v1", latest.PromptVersion);
        Assert.Equal(messages[1].Id, latest.ThroughMessageId);
        // This test intentionally leaves its unique synthetic rows uncommitted; disposing the
        // PostgreSQL transaction rolls them all back, including both summary revisions.
    }

    private static double Percentile(IReadOnlyList<double> orderedSamples, double percentile)
    {
        if (orderedSamples.Count == 0)
        {
            throw new InvalidOperationException("The database load probe produced no latency samples.");
        }

        var index = Math.Clamp(
            (int)Math.Ceiling(percentile * orderedSamples.Count) - 1,
            0,
            orderedSamples.Count - 1);
        return orderedSamples[index];
    }

    private static string DescribeConnectionStringFormat(string connectionString)
    {
        var value = connectionString.Trim();

        if (value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase))
        {
            return "PostgreSQL URI";
        }

        if (value.StartsWith('"') && value.EndsWith('"'))
        {
            return "quoted Npgsql key-value";
        }

        if (value.StartsWith('"'))
        {
            return "Npgsql key-value with a leading quote";
        }

        if (value.Contains('=') && value.Contains(';'))
        {
            return "Npgsql key-value";
        }

        return "unknown";
    }

    private static AppDbContext CreateDbContext(string connectionString)
    {
        connectionString = ConnectionStringNormalizer.Normalize(connectionString);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .Options;

        return new AppDbContext(options);
    }
}
