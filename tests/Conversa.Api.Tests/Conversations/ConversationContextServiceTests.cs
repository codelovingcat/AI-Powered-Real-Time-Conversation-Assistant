using Conversa.Application.Abstractions.Persistence;
using Conversa.Application.Ai;
using Conversa.Application.Common;
using Conversa.Application.Conversations;
using Conversa.Domain.Conversations;
using Microsoft.Extensions.Logging;

namespace Conversa.Api.Tests.Conversations;

public sealed class ConversationContextServiceTests
{
    [Fact]
    public async Task PrepareContextSummaryAsync_SummarizesBoundedOldestBatchAndAdvancesCursor()
    {
        var conversation = CreateConversation();
        var now = DateTimeOffset.UtcNow;
        var pending = Enumerable.Range(0, ConversationContextService.SummaryBatchSize)
            .Select(index => CreateMessage(
                conversation.Id,
                $"older turn {index}",
                now.AddMinutes(-20 + index)))
            .ToArray();
        var recent = CreateRecentMessages(conversation.Id, now);
        var repository = new FakeSummaryRepository(pending);
        var summarizer = new FakeSummarizer("Compact durable summary");
        var logger = new TestLogger<ConversationContextService>();
        var service = new ConversationContextService(
            repository,
            summarizer,
            TimeProvider.System,
            logger);

        var result = await service.PrepareContextSummaryAsync(
            conversation,
            recent,
            CancellationToken.None);

        Assert.Equal("Compact durable summary", result);
        Assert.Equal(1, summarizer.CallCount);
        Assert.Null(summarizer.PreviousSummary);
        Assert.Equal(pending.Select(message => message.OriginalText), summarizer.ReceivedTurns.Select(turn => turn.OriginalText));
        Assert.Equal(conversation.Instruction, summarizer.ActiveInstruction);
        Assert.Equal(conversation.Id, repository.LastConversationId);
        Assert.Equal(conversation.UserId, repository.LastUserId);
        Assert.Equal(0, repository.LastExpectedRevision);
        Assert.Equal(pending[^1].Id, repository.Current!.ThroughMessageId);
        Assert.Equal(pending[^1].CreatedAt, repository.Current.ThroughCreatedAt);
        Assert.Equal("Compact durable summary", repository.Current.Summary);
        Assert.Empty(logger.Messages);
    }

    [Fact]
    public async Task PrepareContextSummaryAsync_SummarizationFailurePreservesLastCommittedSummary()
    {
        var conversation = CreateConversation();
        var now = DateTimeOffset.UtcNow;
        var previousCursor = CreateMessage(
            conversation.Id,
            "old summary cursor",
            now.AddMinutes(-40));
        var existing = ConversationContextSummary.Create(
            conversation.Id,
            "LAST_KNOWN_GOOD_SUMMARY",
            previousCursor.CreatedAt,
            previousCursor.Id,
            now.AddMinutes(-5));
        var pending = Enumerable.Range(0, ConversationContextService.SummaryBatchSize)
            .Select(index => CreateMessage(
                conversation.Id,
                $"PRIVATE_TRANSCRIPT_{index}",
                now.AddMinutes(-20 + index)))
            .ToArray();
        var repository = new FakeSummaryRepository(pending, existing);
        var summarizer = new FakeSummarizer(failure: new AiProviderException("synthetic private transcript failure"));
        var logger = new TestLogger<ConversationContextService>();
        var service = new ConversationContextService(
            repository,
            summarizer,
            TimeProvider.System,
            logger);

        var result = await service.PrepareContextSummaryAsync(
            conversation,
            CreateRecentMessages(conversation.Id, now),
            CancellationToken.None);

        Assert.Equal("LAST_KNOWN_GOOD_SUMMARY", result);
        Assert.Equal(existing, repository.Current);
        Assert.Equal(0, repository.SaveCount);
        Assert.Equal(1, summarizer.CallCount);
        Assert.Single(logger.Messages);
        Assert.Contains(nameof(AiProviderException), logger.Messages[0], StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE_TRANSCRIPT", string.Join(Environment.NewLine, logger.Messages), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrepareContextSummaryAsync_DoesNotCallSummarizerUntilAFullBatchIsAvailable()
    {
        var conversation = CreateConversation();
        var now = DateTimeOffset.UtcNow;
        var pending = Enumerable.Range(0, ConversationContextService.SummaryBatchSize - 1)
            .Select(index => CreateMessage(conversation.Id, $"older {index}", now.AddMinutes(-20 + index)))
            .ToArray();
        var existing = ConversationContextSummary.Create(
            conversation.Id,
            "EXISTING_SUMMARY",
            now.AddMinutes(-30),
            Guid.NewGuid(),
            now.AddMinutes(-10));
        var repository = new FakeSummaryRepository(pending, existing);
        var summarizer = new FakeSummarizer("unexpected replacement");
        var service = new ConversationContextService(
            repository,
            summarizer,
            TimeProvider.System,
            new TestLogger<ConversationContextService>());

        var result = await service.PrepareContextSummaryAsync(
            conversation,
            CreateRecentMessages(conversation.Id, now),
            CancellationToken.None);

        Assert.Equal("EXISTING_SUMMARY", result);
        Assert.Equal(0, summarizer.CallCount);
        Assert.Equal(0, repository.SaveCount);
    }

    private static Conversation CreateConversation()
        => Conversation.Create(
            Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"),
            "Summary test conversation",
            "Preserve the current active instruction.",
            "en",
            "tr",
            DateTimeOffset.UtcNow);

    private static Message[] CreateRecentMessages(Guid conversationId, DateTimeOffset now)
        => Enumerable.Range(0, 12)
            .Select(index => CreateMessage(
                conversationId,
                $"recent turn {index}",
                now.AddMinutes(-12 + index)))
            .ToArray();

    private static Message CreateMessage(Guid conversationId, string text, DateTimeOffset createdAt)
        => Message.CreateFromAssistant(
            conversationId,
            MessageRole.Speaker,
            text,
            $"translation: {text}",
            null,
            null,
            null,
            AiResponseType.Translation,
            false,
            false,
            "synthetic-tests",
            createdAt);

    private sealed class FakeSummaryRepository(
        IReadOnlyList<Message> pending,
        ConversationContextSummary? current = null) : IConversationContextSummaryRepository
    {
        public ConversationContextSummary? Current { get; private set; } = current;
        public int SaveCount { get; private set; }
        public Guid? LastConversationId { get; private set; }
        public Guid? LastUserId { get; private set; }
        public int LastExpectedRevision { get; private set; }

        public Task<ConversationContextSummary?> GetByConversationIdAsync(
            Guid conversationId,
            Guid userId,
            CancellationToken cancellationToken)
        {
            LastConversationId = conversationId;
            LastUserId = userId;
            return Task.FromResult<ConversationContextSummary?>(
                Current?.ConversationId == conversationId ? Current : null);
        }

        public Task<IReadOnlyList<Message>> ListUnsummarizedMessagesAsync(
            Guid conversationId,
            Guid userId,
            DateTimeOffset? afterCreatedAt,
            Guid? afterMessageId,
            DateTimeOffset beforeCreatedAt,
            Guid beforeMessageId,
            int take,
            CancellationToken cancellationToken)
        {
            LastConversationId = conversationId;
            LastUserId = userId;
            return Task.FromResult<IReadOnlyList<Message>>(pending.Take(take).ToArray());
        }

        public Task<bool> TrySaveAsync(
            Guid conversationId,
            Guid userId,
            int expectedRevision,
            string summary,
            DateTimeOffset throughCreatedAt,
            Guid throughMessageId,
            DateTimeOffset updatedAt,
            CancellationToken cancellationToken)
        {
            SaveCount++;
            LastConversationId = conversationId;
            LastUserId = userId;
            LastExpectedRevision = expectedRevision;
            if ((Current?.Revision ?? 0) != expectedRevision)
            {
                return Task.FromResult(false);
            }

            if (Current is not null)
            {
                // This fake only needs the create path in current tests; production
                // updates increment Revision with an atomic conditional UPDATE.
                return Task.FromResult(false);
            }

            Current = ConversationContextSummary.Create(
                conversationId,
                summary,
                throughCreatedAt,
                throughMessageId,
                updatedAt);
            return Task.FromResult(true);
        }
    }

    private sealed class FakeSummarizer(string? result = null, Exception? failure = null) : IConversationSummarizer
    {
        public int CallCount { get; private set; }
        public string? PreviousSummary { get; private set; }
        public string? ActiveInstruction { get; private set; }
        public IReadOnlyList<AiConversationTurn> ReceivedTurns { get; private set; } = Array.Empty<AiConversationTurn>();

        public Task<string> SummarizeAsync(
            string activeInstruction,
            string sourceLanguage,
            string targetLanguage,
            string? previousSummary,
            IReadOnlyList<AiConversationTurn> olderTurns,
            CancellationToken cancellationToken)
        {
            CallCount++;
            PreviousSummary = previousSummary;
            ActiveInstruction = activeInstruction;
            ReceivedTurns = olderTurns;
            if (failure is not null)
            {
                return Task.FromException<string>(failure);
            }

            return Task.FromResult(result ?? string.Empty);
        }
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        void ILogger.Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }
}
