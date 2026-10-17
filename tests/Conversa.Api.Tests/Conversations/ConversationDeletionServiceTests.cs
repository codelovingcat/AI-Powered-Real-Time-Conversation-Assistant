using Conversa.Application.Abstractions.Identity;
using Conversa.Application.Abstractions.Persistence;
using Conversa.Application.Conversations;
using Conversa.Domain.Conversations;

namespace Conversa.Api.Tests.Conversations;

public sealed class ConversationDeletionServiceTests
{
    [Fact]
    public async Task DeleteAsync_IsIdempotentForOwnedConversations()
    {
        var userId = Guid.NewGuid();
        var conversation = Conversation.Create(
            userId,
            "Conversation to delete",
            "Translate naturally.",
            "en",
            "tr",
            DateTimeOffset.UtcNow);
        var repository = new RecordingConversationRepository(conversation);
        var unitOfWork = new RecordingUnitOfWork();
        var service = CreateService(userId, repository, unitOfWork);

        await service.DeleteAsync(conversation.Id, CancellationToken.None);
        await service.DeleteAsync(conversation.Id, CancellationToken.None);

        Assert.Equal(1, repository.RemoveCalls);
        Assert.Equal(1, unitOfWork.SaveCalls);
        Assert.True(repository.IsRemoved);
    }

    [Fact]
    public async Task DeleteAsync_TreatsMissingAndNonOwnedIdsAsSuccessfulNoOps()
    {
        var ownerId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var conversation = Conversation.Create(
            ownerId,
            "Private conversation",
            "Translate naturally.",
            "en",
            "tr",
            DateTimeOffset.UtcNow);
        var repository = new RecordingConversationRepository(conversation);
        var unitOfWork = new RecordingUnitOfWork();
        var service = CreateService(otherUserId, repository, unitOfWork);

        await service.DeleteAsync(conversation.Id, CancellationToken.None);
        await service.DeleteAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(0, repository.RemoveCalls);
        Assert.Equal(0, unitOfWork.SaveCalls);
        Assert.False(repository.IsRemoved);
        Assert.Equal(otherUserId, repository.LastRequestedUserId);
    }

    private static ConversationService CreateService(
        Guid userId,
        RecordingConversationRepository repository,
        RecordingUnitOfWork unitOfWork)
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
        private bool removed;
        public int RemoveCalls { get; private set; }
        public bool IsRemoved => removed;
        public Guid LastRequestedUserId { get; private set; }

        public Task<Conversation?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken)
        {
            LastRequestedUserId = userId;
            return Task.FromResult<Conversation?>(!removed && conversation.Id == id && conversation.UserId == userId ? conversation : null);
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
        {
            RemoveCalls++;
            removed = true;
        }
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
