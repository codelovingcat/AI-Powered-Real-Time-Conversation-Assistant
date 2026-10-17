using Conversa.Application.Abstractions.Identity;
using Conversa.Application.Abstractions.Persistence;
using Conversa.Application.Common;
using Conversa.Application.Conversations;
using Conversa.Domain.Conversations;

namespace Conversa.Api.Tests.Conversations;

public sealed class ConversationLifecycleServiceTests
{
    [Fact]
    public async Task TransitionLifecycleAsync_PersistsIdempotentStartPauseResumeAndFinish()
    {
        var userId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var conversation = Conversation.Create(
            userId,
            "Lifecycle test",
            "Translate naturally.",
            "en",
            "tr",
            now);
        var repository = new RecordingConversationRepository(conversation);
        var unitOfWork = new RecordingUnitOfWork();
        var service = CreateService(userId, repository, unitOfWork);

        Assert.Equal(ConversationLifecycleState.Ready, conversation.LifecycleState);

        var started = await service.TransitionLifecycleAsync(
            conversation.Id,
            ConversationLifecycleAction.Start,
            CancellationToken.None);
        var startedAt = conversation.StartedAt;

        Assert.Equal("active", started.LifecycleState);
        Assert.NotNull(startedAt);

        var repeatedStart = await service.TransitionLifecycleAsync(
            conversation.Id,
            ConversationLifecycleAction.Start,
            CancellationToken.None);
        Assert.Equal("active", repeatedStart.LifecycleState);
        Assert.Equal(startedAt, conversation.StartedAt);

        var paused = await service.TransitionLifecycleAsync(
            conversation.Id,
            ConversationLifecycleAction.Pause,
            CancellationToken.None);
        Assert.Equal("paused", paused.LifecycleState);

        var repeatedPause = await service.TransitionLifecycleAsync(
            conversation.Id,
            ConversationLifecycleAction.Pause,
            CancellationToken.None);
        Assert.Equal("paused", repeatedPause.LifecycleState);

        var resumed = await service.TransitionLifecycleAsync(
            conversation.Id,
            ConversationLifecycleAction.Resume,
            CancellationToken.None);
        Assert.Equal("active", resumed.LifecycleState);

        var finished = await service.TransitionLifecycleAsync(
            conversation.Id,
            ConversationLifecycleAction.Finish,
            CancellationToken.None);
        var completedAt = conversation.CompletedAt;

        Assert.Equal("completed", finished.LifecycleState);
        Assert.NotNull(completedAt);

        var repeatedFinish = await service.TransitionLifecycleAsync(
            conversation.Id,
            ConversationLifecycleAction.Finish,
            CancellationToken.None);
        Assert.Equal("completed", repeatedFinish.LifecycleState);
        Assert.Equal(completedAt, conversation.CompletedAt);
        Assert.Equal(7, unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task TransitionLifecycleAsync_RejectsInvalidTransitionsWithoutSaving()
    {
        var userId = Guid.NewGuid();
        var conversation = Conversation.Create(
            userId,
            "Lifecycle test",
            "Translate naturally.",
            "en",
            "tr",
            DateTimeOffset.UtcNow);
        var repository = new RecordingConversationRepository(conversation);
        var unitOfWork = new RecordingUnitOfWork();
        var service = CreateService(userId, repository, unitOfWork);

        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            service.TransitionLifecycleAsync(
                conversation.Id,
                ConversationLifecycleAction.Pause,
                CancellationToken.None));

        Assert.Contains("Only an active conversation can be paused.", exception.Errors["action"]);
        Assert.Equal(ConversationLifecycleState.Ready, conversation.LifecycleState);
        Assert.Equal(0, unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task TransitionLifecycleAsync_UsesAuthenticatedUserForOwnership()
    {
        var ownerId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var conversation = Conversation.Create(
            ownerId,
            "Private lifecycle",
            "Translate naturally.",
            "en",
            "tr",
            DateTimeOffset.UtcNow);
        var repository = new RecordingConversationRepository(conversation);
        var unitOfWork = new RecordingUnitOfWork();
        var service = CreateService(otherUserId, repository, unitOfWork);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.TransitionLifecycleAsync(
                conversation.Id,
                ConversationLifecycleAction.Finish,
                CancellationToken.None));

        Assert.Equal(ConversationLifecycleState.Ready, conversation.LifecycleState);
        Assert.Equal(0, unitOfWork.SaveCalls);
        Assert.Equal(otherUserId, repository.LastRequestedUserId);
    }

    [Fact]
    public async Task TransitionLifecycleAsync_DoesNotAllowResumeAfterFinish()
    {
        var userId = Guid.NewGuid();
        var conversation = Conversation.Create(
            userId,
            "Finished lifecycle",
            "Translate naturally.",
            "en",
            "tr",
            DateTimeOffset.UtcNow);
        conversation.Finish(DateTimeOffset.UtcNow);
        var repository = new RecordingConversationRepository(conversation);
        var unitOfWork = new RecordingUnitOfWork();
        var service = CreateService(userId, repository, unitOfWork);

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.TransitionLifecycleAsync(
                conversation.Id,
                ConversationLifecycleAction.Resume,
                CancellationToken.None));

        Assert.Equal(ConversationLifecycleState.Completed, conversation.LifecycleState);
        Assert.Equal(0, unitOfWork.SaveCalls);
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

    private sealed class RecordingConversationRepository(Conversation conversation)
        : IConversationRepository
    {
        public Guid LastRequestedUserId { get; private set; }

        public Task<Conversation?> GetByIdAsync(
            Guid id,
            Guid userId,
            CancellationToken cancellationToken)
        {
            LastRequestedUserId = userId;
            return Task.FromResult<Conversation?>(
                conversation.Id == id && conversation.UserId == userId
                    ? conversation
                    : null);
        }

        public Task<IReadOnlyList<Conversation>> ListByUserAsync(
            Guid userId,
            int take,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<Conversation>> ListByUserPageAsync(
            Guid userId,
            int take,
            string? search,
            DateTimeOffset? updatedFrom,
            DateTimeOffset? updatedTo,
            DateTimeOffset? beforeUpdatedAt,
            Guid? beforeId,
            CancellationToken cancellationToken)
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
