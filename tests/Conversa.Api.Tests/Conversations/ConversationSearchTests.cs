using Conversa.Application.Abstractions.Identity;
using Conversa.Application.Abstractions.Persistence;
using Conversa.Application.Common;
using Conversa.Application.Conversations;
using Conversa.Domain.Conversations;

namespace Conversa.Api.Tests.Conversations;

public sealed class ConversationSearchTests
{
    [Fact]
    public async Task Search_is_case_insensitive_and_keeps_authenticated_user_scope()
    {
        var userId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var ownConversation = Conversation.Create(
            userId,
            "Hotel check-in",
            "Translate naturally.",
            "en",
            "tr",
            now);
        var otherConversation = Conversation.Create(
            Guid.NewGuid(),
            "Hotel check-in",
            "Never leak this conversation.",
            "en",
            "tr",
            now.AddMinutes(1));

        var repository = new RecordingConversationRepository(
            [ownConversation, otherConversation]);
        var service = CreateService(userId, repository);

        var page = await service.ListAsync(
            10,
            null,
            "  HOTEL  ",
            null,
            null,
            CancellationToken.None);

        var item = Assert.Single(page.Items);
        Assert.Equal(ownConversation.Id, item.Id);
        Assert.Equal(userId, repository.LastUserId);
        Assert.Equal("HOTEL", repository.LastSearch);
    }

    [Fact]
    public async Task Search_results_remain_paginated_and_empty_final_page_has_no_cursor()
    {
        var userId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var items = new[]
        {
            Conversation.Create(userId, "Hotel one", "Translate naturally.", "en", "tr", now),
            Conversation.Create(userId, "Hotel two", "Translate naturally.", "en", "tr", now.AddMinutes(-1)),
            Conversation.Create(userId, "Hotel three", "Translate naturally.", "en", "tr", now.AddMinutes(-2)),
            Conversation.Create(Guid.NewGuid(), "Hotel other user", "Do not expose.", "en", "tr", now.AddMinutes(2))
        };

        var repository = new RecordingConversationRepository(items);
        var service = CreateService(userId, repository);

        var firstPage = await service.ListAsync(
            2,
            null,
            "hotel",
            null,
            null,
            CancellationToken.None);

        Assert.Equal(2, firstPage.Items.Count);
        Assert.NotNull(firstPage.NextCursor);

        var secondPage = await service.ListAsync(
            2,
            firstPage.NextCursor,
            "hotel",
            null,
            null,
            CancellationToken.None);

        Assert.Single(secondPage.Items);
        Assert.Null(secondPage.NextCursor);
        Assert.All(
            firstPage.Items.Concat(secondPage.Items),
            item => Assert.Equal(userId, item.UserId));
    }

    [Fact]
    public async Task Updated_date_filters_use_inclusive_start_and_exclusive_end()
    {
        var userId = Guid.NewGuid();
        var from = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddDays(1);

        var included = Conversation.Create(
            userId,
            "Included",
            "Translate naturally.",
            "en",
            "tr",
            from);
        var excludedBefore = Conversation.Create(
            userId,
            "Before",
            "Translate naturally.",
            "en",
            "tr",
            from.AddTicks(-1));
        var excludedAtEnd = Conversation.Create(
            userId,
            "At end",
            "Translate naturally.",
            "en",
            "tr",
            to);

        var repository = new RecordingConversationRepository(
            [included, excludedBefore, excludedAtEnd]);
        var service = CreateService(userId, repository);

        var page = await service.ListAsync(
            10,
            null,
            null,
            from,
            to,
            CancellationToken.None);

        var item = Assert.Single(page.Items);
        Assert.Equal(included.Id, item.Id);
    }

    [Fact]
    public async Task Oversized_search_is_rejected_before_repository_query()
    {
        var userId = Guid.NewGuid();
        var repository = new RecordingConversationRepository([]);
        var service = CreateService(userId, repository);

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.ListAsync(
                10,
                null,
                new string('x', ConversationInputValidator.MaxSearchLength + 1),
                null,
                null,
                CancellationToken.None));

        Assert.Equal(0, repository.ListPageCalls);
    }

    [Fact]
    public async Task Invalid_updated_date_range_is_rejected_before_repository_query()
    {
        var userId = Guid.NewGuid();
        var repository = new RecordingConversationRepository([]);
        var service = CreateService(userId, repository);
        var from = new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddDays(-1);

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.ListAsync(
                10,
                null,
                null,
                from,
                to,
                CancellationToken.None));

        Assert.Equal(0, repository.ListPageCalls);
    }

    [Fact]
    public async Task Search_does_not_treat_like_wildcards_as_pattern_controls()
    {
        var userId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var literal = Conversation.Create(
            userId,
            "100% ready",
            "Translate naturally.",
            "en",
            "tr",
            now);
        var broader = Conversation.Create(
            userId,
            "1000 ready",
            "Translate naturally.",
            "en",
            "tr",
            now.AddMinutes(-1));

        var repository = new RecordingConversationRepository([literal, broader]);
        var service = CreateService(userId, repository);

        var page = await service.ListAsync(
            10,
            null,
            "100%",
            null,
            null,
            CancellationToken.None);

        var item = Assert.Single(page.Items);
        Assert.Equal(literal.Id, item.Id);
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
        IReadOnlyList<Conversation> items) : IConversationRepository
    {
        public int ListPageCalls { get; private set; }
        public Guid LastUserId { get; private set; }
        public string? LastSearch { get; private set; }

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
            string? search,
            DateTimeOffset? updatedFrom,
            DateTimeOffset? updatedTo,
            DateTimeOffset? beforeUpdatedAt,
            Guid? beforeId,
            CancellationToken cancellationToken)
        {
            ListPageCalls++;
            LastUserId = userId;
            LastSearch = search;

            var query = items.Where(item => item.UserId == userId);

            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(item =>
                    item.Title.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)
                    || item.SourceLanguage.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)
                    || item.TargetLanguage.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase));
            }

            if (updatedFrom is not null)
            {
                query = query.Where(item => item.UpdatedAt >= updatedFrom.Value);
            }

            if (updatedTo is not null)
            {
                query = query.Where(item => item.UpdatedAt < updatedTo.Value);
            }

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
}
