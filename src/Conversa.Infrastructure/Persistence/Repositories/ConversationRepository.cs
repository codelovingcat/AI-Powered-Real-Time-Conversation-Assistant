using Conversa.Application.Abstractions.Persistence;
using Conversa.Domain.Conversations;
using Microsoft.EntityFrameworkCore;

namespace Conversa.Infrastructure.Persistence.Repositories;

internal sealed class ConversationRepository(AppDbContext db) : IConversationRepository
{
    public Task<Conversation?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken)
        => db.Conversations.FirstOrDefaultAsync(
            conversation => conversation.Id == id && conversation.UserId == userId,
            cancellationToken);

    public async Task<IReadOnlyList<Conversation>> ListByUserAsync(
        Guid userId,
        int take,
        CancellationToken cancellationToken)
        => await db.Conversations
            .Where(conversation => conversation.UserId == userId)
            .OrderByDescending(conversation => conversation.UpdatedAt)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(Conversation conversation, CancellationToken cancellationToken)
        => await db.Conversations.AddAsync(conversation, cancellationToken);

    public void Remove(Conversation conversation) => db.Conversations.Remove(conversation);
}
