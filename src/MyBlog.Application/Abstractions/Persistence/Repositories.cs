using MyBlog.Domain.Common;

namespace MyBlog.Application.Abstractions.Persistence;

public interface IReadRepository<T> where T : class
{
    /// <summary>Id bo'yicha (ownership va soft delete filtrlari bilan).</summary>
    Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<T?> FirstOrDefaultAsync(ISpecification<T> specification, CancellationToken cancellationToken = default);
    Task<TResult?> FirstOrDefaultAsync<TResult>(ISpecification<T, TResult> specification, CancellationToken cancellationToken = default);
    Task<List<T>> ListAsync(ISpecification<T> specification, CancellationToken cancellationToken = default);
    Task<List<TResult>> ListAsync<TResult>(ISpecification<T, TResult> specification, CancellationToken cancellationToken = default);
    Task<int> CountAsync(ISpecification<T> specification, CancellationToken cancellationToken = default);
    Task<bool> AnyAsync(ISpecification<T> specification, CancellationToken cancellationToken = default);
}

/// <summary>Faqat aggregate root'lar uchun yozish repository'si.</summary>
public interface IRepository<T> : IReadRepository<T> where T : class, IAggregateRoot
{
    void Add(T entity);
    void Update(T entity);

    /// <summary>ISoftDeletable bo'lsa soft delete, aks holda haqiqiy o'chirish.</summary>
    void Remove(T entity);
}

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Bir nechta SaveChanges/ExecuteUpdate'ni bitta tranzaksiyada bajaradi.</summary>
    Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken = default);
}
