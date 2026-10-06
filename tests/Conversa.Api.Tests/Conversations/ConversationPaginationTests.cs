using Conversa.Application.Abstractions.Identity;
using Conversa.Application.Abstractions.Persistence;
using Conversa.Application.Common;
using Conversa.Application.Conversations;
using Conversa.Domain.Conversations;

namespace Conversa.Api.Tests.Conversations;

public sealed class ConversationPaginationTests
{
    [Fact]
    public async Task Conversation_pages_are_bounded_and_keep_authenticated_user_scope()
    {
        var userId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var conversations = Enumerable.Range(0, 3)
            .Select(index => Conversation.Create(
                userId,
                $"Conversation {index}",
                "Translate English into natural Turkish.",
                "en",
                "tr",
                now.AddMinutes(-index)))
            .Append(
                Conversation.Create(
                    Guid.NewGuid(),
                    "Other user's conversation",
                    "This must never appear in the page.",
                    "en",
                    "tr",
                    now.AddMinutes(1)))
            .ToArray();

        var repository = new RecordingConversationRepository(conversations);
        var service = CreateService(userId, repository);

        var firstPage = await service.ListAsync(2, null, CancellationToken.None);

        Assert.Equal(2, firstPage.Items.Count);
        Assert.All(firstPage.Items, item => Assert.Equal(userId, item.UserId));
        Assert.NotNull(firstPage.NextCursor);
        Assert.Equal(userId, repository.LastUserId);
        Assert.Equal(3, repository.LastTake);

        var secondPage = await service.ListAsync(
            2,
            firstPage.NextCursor,
            CancellationToken.None);

        Assert.Single(secondPage.Items);
        Assert.Null(secondPage.NextCursor);
        Assert.Equal(userId, repository.LastUserId);
        Assert.NotNull(repository.LastBeforeUpdatedAt);
        Assert.NotNull(repository.LastBeforeId);
    }

    [Fact]
    public async Task Invalid_cursor_is_rejected()
    {
        var userId = Guid.NewGuid();
        var repository = new RecordingConversationRepository([]);
        var service = CreateService(userId, repository);

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.ListAsync(10, "not-a-cursor", CancellationToken.None));

        Assert.Null(repository.LastBeforeUpdatedAt);
        Assert.Null(repository.LastBeforeId);
    }

    [Fact]
    public async Task Message_pages_are_returned_oldest_to_newest_and_preserve_ownership_check()
    {
        var userId = Guid.NewGuid();
        var conversation = Conversation.Create(
            userId,
            "Paged conversation",
            "Translate naturally.",
            "en",
            "tr",
            DateTimeOffset.UtcNow);

        var messages = new[]
        {
            Message.CreateFromAssistant(
                conversation.Id,
                MessageRole.Speaker,
                "one",
                "bir",
                null,
                null,
                null,
                AiResponseType.Translation,
                false,
                false,
                "gemini",
                conversation.CreatedAt.AddMinutes(-3)),
            Message.CreateFromAssistant(
                conversation.Id,
                MessageRole.Speaker,
                "two",
                "iki",
                null,
                null,
                null,
                AiResponseType.Translation,
                false,
                false,
                "gemini",
                conversation.CreatedAt.AddMinutes(-2)),
            Message.CreateFromAssistant(
                conversation.Id,
                MessageRole.Speaker,
                "three",
                "üç",
                null,
                null,
                null,
                AiResponseType.Translation,
                false,
                false,
                "gemini",
                conversation.CreatedAt.AddMinutes(-1))
        };

        var conversationRepository = new RecordingConversationRepository([conversation]);
        var messageRepository = new RecordingMessageRepository(messages);
        var service = new ConversationService(
            new TestCurrentUser(userId),
            conversationRepository,
            messageRepository,
            unitOfWork: null!,
            TimeProvider.System,
            new ConversationInputValidator());

        var firstPage = await service.ListMessagesAsync(
            conversation.Id,
            2,
            null,
            CancellationToken.None);

        Assert.Equal(["two", "three"], firstPage.Items.Select(item => item.OriginalText));
        Assert.NotNull(firstPage.NextCursor);
        Assert.Equal(conversation.Id, messageRepository.LastConversationId);

        var secondPage = await service.ListMessagesAsync(
            conversation.Id,
            2,
            firstPage.NextCursor,
            CancellationToken.None);

        Assert.Equal(["one"], secondPage.Items.Select(item => item.OriginalText));
        Assert.Null(secondPage.NextCursor);
    }

    private static ConversationService CreateService(
        Guid userId,
        RecordingConversationRepository repository)
        => new(
            new TestCurrentUser(userId),
            repository,
            messages: null!,
            unitOfWork: null!,
            TimeProvider.System,
            new ConversationInputValidator());

    private sealed class TestCurrentUser(Guid userId) : ICurrentUser
    {
        public Guid UserId => userId;
    }

    private sealed class RecordingConversationRepository(
        IReadOnlyList<Conversation> items)
        : IConversationRepository
    {
        public Guid LastUserId { get; private set; }
        public int LastTake { get; private set; }
        public DateTimeOffset? LastBeforeUpdatedAt { get; private set; }
        public Guid? LastBeforeId { get; private set; }

        public Task<Conversation?> GetByIdAsync(
            Guid id,
            Guid userId,
            CancellationToken cancellationToken)
            => Task.FromResult<Conversation?>(
                items.FirstOrDefault(item => item.Id == id && item.UserId == userId));

        public Task<IReadOnlyList<Conversation>> ListByUserAsync(
            Guid userId,
            int take,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<Conversation>>(
                items.Where(item => item.UserId == userId).Take(take).ToArray());

        public Task<IReadOnlyList<Conversation>> ListByUserPageAsync(
            Guid userId,
            int take,
            DateTimeOffset? beforeUpdatedAt,
            Guid? beforeId,
            CancellationToken cancellationToken)
        {
            LastUserId = userId;
            LastTake = take;
            LastBeforeUpdatedAt = beforeUpdatedAt;
            LastBeforeId = beforeId;

            var query = items.Where(item => item.UserId == userId);

            if (beforeUpdatedAt is not null && beforeId is not null)
            {
                query = query.Where(item =>
                    item.UpdatedAt < beforeUpdatedAt.Value
                    || (item.UpdatedAt == beforeUpdatedAt.Value
                        && item.Id.CompareTo(beforeId.Value) < 0));
            }

            return Task.FromResult<IReadOnlyList<Conversation>>(
                query
                    .OrderByDescending(item => item.UpdatedAt)
                    .ThenByDescending(item => item.Id)
                    .Take(take)
                    .ToArray());
        }

        public Task AddAsync(Conversation conversation, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public void Remove(Conversation conversation)
            => throw new NotSupportedException();
    }

    private sealed class RecordingMessageRepository(
        IReadOnlyList<Message> items)
        : IMessageRepository
    {
        public Guid LastConversationId { get; private set; }

        public Task<IReadOnlyList<Message>> ListByConversationAsync(
            Guid conversationId,
            int take,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<Message>>(
                items.Where(item => item.ConversationId == conversationId).Take(take).ToArray());

        public Task<IReadOnlyList<Message>> ListByConversationPageAsync(
            Guid conversationId,
            int take,
            DateTimeOffset? beforeCreatedAt,
            Guid? beforeId,
            CancellationToken cancellationToken)
        {
            LastConversationId = conversationId;
            var query = items.Where(item => item.ConversationId == conversationId);

            if (beforeCreatedAt is not null && beforeId is not null)
            {
                query = query.Where(item =>
                    item.CreatedAt < beforeCreatedAt.Value
                    || (item.CreatedAt == beforeCreatedAt.Value
                        && item.Id.CompareTo(beforeId.Value) < 0));
            }

            return Task.FromResult<IReadOnlyList<Message>>(
                query
                    .OrderByDescending(item => item.CreatedAt)
                    .ThenByDescending(item => item.Id)
                    .Take(take)
                    .ToArray());
        }

        public Task<IReadOnlyList<Message>> ListRecentAsync(
            Guid conversationId,
            int take,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<Message>>([]);

        public Task AddAsync(Message message, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
