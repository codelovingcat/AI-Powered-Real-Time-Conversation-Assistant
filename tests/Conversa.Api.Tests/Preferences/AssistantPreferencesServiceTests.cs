using Conversa.Application.Abstractions.Identity;
using Conversa.Application.Abstractions.Persistence;
using Conversa.Application.Common;
using Conversa.Application.Preferences;
using Conversa.Domain.Preferences;

namespace Conversa.Api.Tests.Preferences;

public sealed class AssistantPreferencesServiceTests
{
    [Fact]
    public async Task GetAsync_WhenNoSavedValue_ReturnsDocumentedDefaultsWithoutWriting()
    {
        var userId = Guid.NewGuid();
        var repository = new FakePreferencesRepository();
        var unitOfWork = new RecordingUnitOfWork();
        var service = CreateService(userId, repository, unitOfWork);

        var result = await service.GetAsync(CancellationToken.None);

        Assert.Equal(AssistantPreferencesDto.Default, result);
        Assert.Empty(repository.Stored);
        Assert.Equal(0, unitOfWork.SaveCalls);
        Assert.Equal(userId, Assert.Single(repository.Lookups));
    }

    [Fact]
    public async Task UpdateAsync_PersistsOnlyForTheAuthenticatedUser()
    {
        var currentUserId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var otherUserPreferences = UserAssistantPreferences.Create(
            otherUserId,
            "detailed",
            "formal",
            "minimal",
            false,
            DateTimeOffset.UtcNow);
        var repository = new FakePreferencesRepository(otherUserPreferences);
        var unitOfWork = new RecordingUnitOfWork();
        var service = CreateService(currentUserId, repository, unitOfWork);

        var result = await service.UpdateAsync(
            new UpdateAssistantPreferencesCommand(
                "concise",
                "casual",
                "detailed",
                false),
            CancellationToken.None);

        Assert.Equal(new AssistantPreferencesDto("concise", "casual", "detailed", false), result);
        Assert.True(repository.Stored.ContainsKey(currentUserId));
        Assert.Equal("detailed", repository.Stored[otherUserId].ResponseLength);
        Assert.Equal("formal", repository.Stored[otherUserId].Tone);
        Assert.Equal(1, unitOfWork.SaveCalls);
        Assert.All(repository.Lookups, userId => Assert.Equal(currentUserId, userId));
    }

    [Theory]
    [InlineData("unsupported", "contextual", "balanced")]
    [InlineData("balanced", "not-a-tone", "balanced")]
    [InlineData("balanced", "contextual", "unbounded-prompt")]
    public async Task UpdateAsync_RejectsUnknownOptionsWithoutSaving(
        string responseLength,
        string tone,
        string explanationLevel)
    {
        var repository = new FakePreferencesRepository();
        var unitOfWork = new RecordingUnitOfWork();
        var service = CreateService(Guid.NewGuid(), repository, unitOfWork);

        await Assert.ThrowsAsync<ValidationException>(() => service.UpdateAsync(
            new UpdateAssistantPreferencesCommand(
                responseLength,
                tone,
                explanationLevel,
                true),
            CancellationToken.None));

        Assert.Empty(repository.Stored);
        Assert.Equal(0, unitOfWork.SaveCalls);
    }

    private static AssistantPreferencesService CreateService(
        Guid userId,
        FakePreferencesRepository repository,
        RecordingUnitOfWork unitOfWork)
        => new(new TestCurrentUser(userId), repository, unitOfWork, TimeProvider.System);

    private sealed class TestCurrentUser(Guid userId) : ICurrentUser
    {
        public Guid UserId => userId;
    }

    private sealed class FakePreferencesRepository(params UserAssistantPreferences[] initial)
        : IUserAssistantPreferencesRepository
    {
        public Dictionary<Guid, UserAssistantPreferences> Stored { get; } =
            initial.ToDictionary(item => item.UserId);

        public List<Guid> Lookups { get; } = [];

        public Task<UserAssistantPreferences?> GetByUserIdAsync(
            Guid userId,
            CancellationToken cancellationToken)
        {
            Lookups.Add(userId);
            Stored.TryGetValue(userId, out var preferences);
            return Task.FromResult(preferences);
        }

        public Task AddAsync(
            UserAssistantPreferences preferences,
            CancellationToken cancellationToken)
        {
            Stored.Add(preferences.UserId, preferences);
            return Task.CompletedTask;
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
