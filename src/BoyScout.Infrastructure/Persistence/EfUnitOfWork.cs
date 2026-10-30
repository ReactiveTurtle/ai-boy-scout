using BoyScout.Common.Persistence;

namespace BoyScout.Infrastructure.Persistence;

public sealed class EfUnitOfWork(BoyScoutDbContext context) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);

    public async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var result = await operation(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }
}
