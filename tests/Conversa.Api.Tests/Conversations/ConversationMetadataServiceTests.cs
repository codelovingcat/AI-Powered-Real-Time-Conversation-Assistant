using Conversa.Application.Abstractions.Identity;
using Conversa.Application.Abstractions.Persistence;
using Conversa.Application.Common;
using Conversa.Application.Conversations;
using Conversa.Domain.Conversations;

namespace Conversa.Api.Tests.Conversations;

public sealed class ConversationMetadataServiceTests
{
    [Fact]
    public async Task UpdateAsync_RenamesOwnedConversationAndUpdatesTimestamp()
    {
        var userId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        var conversation = Conversation.Create(userId, "Original title", "Translate naturally.", "en", "tr", createdAt);
        var repository = new RecordingConversationRepository(conversation);
        var unitOfWork = new RecordingUnitOfWork();
        var service = CreateService(userId, repository, unitOfWork);

        var result = await service.UpdateAsync(
            new UpdateConversationCommand(conversation.Id, "  Renamed title  ", null, null, null, conversation.UpdatedAt),
            CancellationToken.None);

        Assert.Equal("Renamed title", result.Title);
        Assert.True(result.UpdatedAt > createdAt);
        Assert.Equal(1, unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task UpdateAsync_RejectsStaleTimestampWithoutChangingTitle()
    {
        var userId = Guid.NewGuid();
        var conversation = Conversation.Create(userId, "Original title", "Translate naturally.", "en", "tr", DateTimeOffset.UtcNow);
        var repository = new RecordingConversationRepository(conversation);
        var unitOfWork = new RecordingUnitOfWork();
        var service = CreateService(userId, repository, unitOfWork);

        await Assert.ThrowsAsync<ConflictException>(() => service.UpdateAsync(
            new UpdateConversationCommand(conversation.Id, "Lost update", null, null, null, conversation.UpdatedAt.AddSeconds(-1)),
            CancellationToken.None));

        Assert.Equal("Original title", conversation.Title);
        Assert.Equal(0, unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task UpdateAsync_DoesNotExposeConversationOwnedByAnotherUser()
    {
        var ownerId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var conversation = Conversation.Create(ownerId, "Private title", "Translate naturally.", "en", "tr", DateTimeOffset.UtcNow);
        var repository = new RecordingConversationRepository(conversation);
        var unitOfWork = new RecordingUnitOfWork();
        var service = CreateService(otherUserId, repository, unitOfWork);

        await Assert.ThrowsAsync<NotFoundException>(() => service.UpdateAsync(
            new UpdateConversationCommand(conversation.Id, "Intruder edit", null, null, null, conversation.UpdatedAt),
            CancellationToken.None));

        Assert.Equal("Private title", conversation.Title);
        Assert.Equal(0, unitOfWork.SaveCalls);
        Assert.Equal(otherUserId, repository.LastRequestedUserId);
    }

    private static ConversationService CreateService(Guid userId, RecordingConversationRepository repository, RecordingUnitOfWork unitOfWork)
        => new(
            new TestCurrentUser(userId),
            repository,
            messages: null!,
            unitOfWork,
            TimeProvider.System,
            new ConversationInputValidator());

    private sealed class TestCurrentUser(Guid userId) : ICurrentUser
    {
        public Guid UserId => userId;
    }

    private sealed class RecordingConversationRepository(Conversation conversation) : IConversationRepository
    {
        public Guid LastRequestedUserId { get; private set; }

        public Task<Conversation?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken)
        {
            LastRequestedUserId = userId;
            return Task.FromResult<Conversation?>(conversation.Id == id && conversation.UserId == userId ? conversation : null);
        }

        public Task<IReadOnlyList<Conversation>> ListByUserAsync(Guid userId, int take, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<Conversation>> ListByUserPageAsync(
            Guid userId, int take, string? search, DateTimeOffset? updatedFrom, DateTimeOffset? updatedTo,
            DateTimeOffset? beforeUpdatedAt, Guid? beforeId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task AddAsync(Conversation conversation, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public void Remove(Conversation conversation)
            => throw new NotSupportedException();
    }

    private sealed class RecordingUnitOfWork : IUnitOfWork
    {
        public int SaveCalls { get; private set; }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveCalls++;
            return Task.CompletedTask;
        }
    }
}
