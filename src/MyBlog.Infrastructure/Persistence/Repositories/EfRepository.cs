using Microsoft.EntityFrameworkCore;
using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Domain.Common;

namespace MyBlog.Infrastructure.Persistence.Repositories;

/// <summary>
/// Umumiy o'qish repository'si (har qanday entity uchun). Modul repository'lari shundan meros olib,
/// maxsus so'rovlarni qo'shishi mumkin.
/// </summary>
internal class EfReadRepository<T>(AppDbContext dbContext) : IReadRepository<T> where T : class
{
    protected AppDbContext DbContext { get; } = dbContext;
    protected DbSet<T> Set => DbContext.Set<T>();

    public virtual async Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await Set.FindAsync([id], cancellationToken);

    public Task<T?> FirstOrDefaultAsync(ISpecification<T> specification, CancellationToken cancellationToken = default) =>
        SpecificationEvaluator.Apply(Set, specification).FirstOrDefaultAsync(cancellationToken);

    public Task<TResult?> FirstOrDefaultAsync<TResult>(ISpecification<T, TResult> specification, CancellationToken cancellationToken = default) =>
        SpecificationEvaluator.Apply(Set, specification).FirstOrDefaultAsync(cancellationToken);

    public Task<List<T>> ListAsync(ISpecification<T> specification, CancellationToken cancellationToken = default) =>
        SpecificationEvaluator.Apply(Set, specification).ToListAsync(cancellationToken);

    public Task<List<TResult>> ListAsync<TResult>(ISpecification<T, TResult> specification, CancellationToken cancellationToken = default) =>
        SpecificationEvaluator.Apply(Set, specification).ToListAsync(cancellationToken);

    public Task<int> CountAsync(ISpecification<T> specification, CancellationToken cancellationToken = default) =>
        SpecificationEvaluator.Apply(Set, specification, forCount: true).CountAsync(cancellationToken);

    public Task<bool> AnyAsync(ISpecification<T> specification, CancellationToken cancellationToken = default) =>
        SpecificationEvaluator.Apply(Set, specification, forCount: true).AnyAsync(cancellationToken);
}

/// <summary>Aggregate root'lar uchun yozish repository'si. O'zgarishlar IUnitOfWork.SaveChangesAsync bilan saqlanadi.</summary>
internal class EfRepository<T>(AppDbContext dbContext) : EfReadRepository<T>(dbContext), IRepository<T>
    where T : class, IAggregateRoot
{
    public void Add(T entity) => Set.Add(entity);

    public void Update(T entity) => Set.Update(entity);

    /// <summary>
    /// ISoftDeletable bo'lsa DbSet.Remove chaqirilmaydi: SoftDeletableEntity.MarkDeleted() (domain hook) yoki
    /// IsDeleted=true qo'yiladi, DeletedAt/DeletedBy'ni SoftDeleteInterceptor to'ldiradi. Shunda owned
    /// type'lar va cascade dependent'lar tasodifan o'chib ketmaydi. Aks holda haqiqiy o'chirish.
    /// </summary>
    public void Remove(T entity)
    {
        if (entity is not ISoftDeletable)
        {
            Set.Remove(entity);
            return;
        }

        var entry = DbContext.Entry(entity);
        if (entry.State == EntityState.Detached)
            Set.Attach(entity);

        if (entity is SoftDeletableEntity softDeletable)
            softDeletable.MarkDeleted();
        else
            entry.Property(nameof(ISoftDeletable.IsDeleted)).CurrentValue = true;
    }
}
