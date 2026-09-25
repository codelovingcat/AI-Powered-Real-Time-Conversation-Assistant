using Conversa.Application.Abstractions.Identity;
using Conversa.Application.Abstractions.Persistence;
using Conversa.Application.Common;
using Conversa.Application.Conversations;
using Conversa.Domain.Conversations;

namespace Conversa.Api.Tests.Security;

public sealed class ConversationOwnershipTests
{
    [Fact]
    public async Task GetAsync_UsesAuthenticatedUserIdForRepositoryLookup()
    {
        var userId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var currentUser = new TestCurrentUser(userId);
        var repository = new RecordingConversationRepository();

        var service = new ConversationService(
            currentUser,
            repository,
            messages: null!,
            unitOfWork: null!,
            TimeProvider.System);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.GetAsync(conversationId, CancellationToken.None));

        Assert.Equal(conversationId, repository.RequestedConversationId);
        Assert.Equal(userId, repository.RequestedUserId);
    }

    private sealed class TestCurrentUser(Guid userId) : ICurrentUser
    {
        public Guid UserId => userId;
    }

    private sealed class RecordingConversationRepository : IConversationRepository
    {
        public Guid RequestedConversationId { get; private set; }
        public Guid RequestedUserId { get; private set; }

        public Task<Conversation?> GetByIdAsync(
            Guid id,
            Guid userId,
            CancellationToken cancellationToken)
        {
            RequestedConversationId = id;
            RequestedUserId = userId;
            return Task.FromResult<Conversation?>(null);
        }

        public Task<IReadOnlyList<Conversation>> ListByUserAsync(
            Guid userId,
            int take,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task AddAsync(Conversation conversation, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public void Remove(Conversation conversation)
            => throw new NotSupportedException();
    }
}
