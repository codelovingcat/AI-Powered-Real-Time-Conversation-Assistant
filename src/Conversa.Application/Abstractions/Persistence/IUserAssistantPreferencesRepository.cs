using Conversa.Domain.Preferences;

namespace Conversa.Application.Abstractions.Persistence;

public interface IUserAssistantPreferencesRepository
{
    Task<UserAssistantPreferences?> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken);

    Task AddAsync(
        UserAssistantPreferences preferences,
        CancellationToken cancellationToken);
}
