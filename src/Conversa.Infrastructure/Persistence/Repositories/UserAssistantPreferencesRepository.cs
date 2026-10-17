using Conversa.Application.Abstractions.Persistence;
using Conversa.Domain.Preferences;
using Microsoft.EntityFrameworkCore;

namespace Conversa.Infrastructure.Persistence.Repositories;

internal sealed class UserAssistantPreferencesRepository(AppDbContext db)
    : IUserAssistantPreferencesRepository
{
    public Task<UserAssistantPreferences?> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken)
        => db.UserAssistantPreferences.SingleOrDefaultAsync(
            preferences => preferences.UserId == userId,
            cancellationToken);

    public async Task AddAsync(
        UserAssistantPreferences preferences,
        CancellationToken cancellationToken)
        => await db.UserAssistantPreferences.AddAsync(preferences, cancellationToken);
}
