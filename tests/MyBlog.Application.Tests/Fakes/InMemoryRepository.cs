using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Domain.Common;

namespace MyBlog.Application.Tests.Fakes;

/// <summary>
/// Specification'larni xotirada bajaradigan repository (criteria, tartib, sahifalash, projection).
/// Query filter'lar (ownership/soft delete) simulyatsiya qilinmaydi — soft delete'dan tashqari.
/// </summary>
internal class InMemoryReadRepository<T>(List<T> items) : IReadRepository<T> where T : class
{
    public List<T> Items { get; } = items;

    public Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Visible().FirstOrDefault(e => e is Entity entity && entity.Id == id));

    public Task<T?> FirstOrDefaultAsync(ISpecification<T> specification, CancellationToken cancellationToken = default) =>
        Task.FromResult(Apply(specification).FirstOrDefault());

    public Task<TResult?> FirstOrDefaultAsync<TResult>(ISpecification<T, TResult> specification, CancellationToken cancellationToken = default) =>
        Task.FromResult(Apply(specification).Select(specification.Selector.Compile()).FirstOrDefault());

    public Task<List<T>> ListAsync(ISpecification<T> specification, CancellationToken cancellationToken = default) =>
        Task.FromResult(Apply(specification).ToList());

    public Task<List<TResult>> ListAsync<TResult>(ISpecification<T, TResult> specification, CancellationToken cancellationToken = default) =>
        Task.FromResult(Apply(specification).Select(specification.Selector.Compile()).ToList());

    public Task<int> CountAsync(ISpecification<T> specification, CancellationToken cancellationToken = default) =>
        Task.FromResult(Filter(specification).Count());

    public Task<bool> AnyAsync(ISpecification<T> specification, CancellationToken cancellationToken = default) =>
        Task.FromResult(Filter(specification).Any());

    private IEnumerable<T> Visible() => Items.Where(e => e is not ISoftDeletable { IsDeleted: true });

    private IEnumerable<T> Filter(ISpecification<T> specification)
    {
        var query = specification.IgnoreSoftDeleteFilter ? Items.AsEnumerable() : Visible();
        foreach (var criteria in specification.Criteria)
            query = query.Where(criteria.Compile());
        return query;
    }

    private IEnumerable<T> Apply(ISpecification<T> specification)
    {
        var query = Filter(specification);

        if (specification.OrderBy.Count > 0)
        {
            var (firstKey, firstDescending) = specification.OrderBy[0];
            var ordered = firstDescending ? query.OrderByDescending(firstKey.Compile()) : query.OrderBy(firstKey.Compile());
            foreach (var (key, descending) in specification.OrderBy.Skip(1))
                ordered = descending ? ordered.ThenByDescending(key.Compile()) : ordered.ThenBy(key.Compile());
            query = ordered;
        }

        if (specification.Skip is > 0)
            query = query.Skip(specification.Skip.Value);
        if (specification.Take is { } take)
            query = query.Take(take);

        return query.ToList();
    }
}

internal sealed class InMemoryRepository<T>(List<T> items) : InMemoryReadRepository<T>(items), IRepository<T>
    where T : class, IAggregateRoot
{
    public InMemoryRepository() : this([])
    {
    }

    public void Add(T entity) => Items.Add(entity);

    public void Update(T entity)
    {
    }

    public void Remove(T entity)
    {
        if (entity is SoftDeletableEntity softDeletable)
            softDeletable.MarkDeleted();
        else
            Items.Remove(entity);
    }
}

internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public int SaveCount { get; private set; }
    public int TransactionCount { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveCount++;
        return Task.FromResult(1);
    }

    public async Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        TransactionCount++;
        return await operation(cancellationToken);
    }
}

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
