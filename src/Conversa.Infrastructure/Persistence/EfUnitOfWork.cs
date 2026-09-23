using Conversa.Application.Abstractions.Persistence;

namespace Conversa.Infrastructure.Persistence;

internal sealed class EfUnitOfWork(AppDbContext db) : IUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken cancellationToken)
        => db.SaveChangesAsync(cancellationToken);
}
