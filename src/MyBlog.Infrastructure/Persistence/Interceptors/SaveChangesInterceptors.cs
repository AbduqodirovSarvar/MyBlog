using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Domain.Common;
using MyBlog.Infrastructure.DataIsolation;

namespace MyBlog.Infrastructure.Persistence.Interceptors;

/// <summary>SavingChanges'da sinxron ishlaydigan interceptor'lar uchun asos.</summary>
internal abstract class SavingChangesInterceptorBase : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is not null)
            Apply(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
            Apply(eventData.Context);
        return ValueTask.FromResult(result);
    }

    protected abstract void Apply(DbContext context);

    protected static void SetValue(EntityEntry entry, string propertyName, object? value) =>
        entry.Property(propertyName).CurrentValue = value;
}

/// <summary>
/// Ownership qoidalari: yangi entity'ga OwnerId avtomatik qo'yiladi, boshqa foydalanuvchi nomidan
/// yaratish va OwnerId'ni o'zgartirish taqiqlanadi.
/// </summary>
internal sealed class OwnershipInterceptor(ICurrentUser currentUser, IDataIsolationContext isolation)
    : SavingChangesInterceptorBase
{
    protected override void Apply(DbContext context)
    {
        foreach (var entry in context.ChangeTracker.Entries<IOwnedEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    HandleAdded(entry);
                    break;
                case EntityState.Modified when entry.Property(nameof(IOwnedEntity.OwnerId)).IsModified:
                    var original = (Guid)entry.Property(nameof(IOwnedEntity.OwnerId)).OriginalValue!;
                    if (original != entry.Entity.OwnerId)
                        throw new ForbiddenOwnershipException($"OwnerId of {entry.Metadata.DisplayName()} cannot be changed.");
                    break;
            }
        }
    }

    private void HandleAdded(EntityEntry<IOwnedEntity> entry)
    {
        var userId = currentUser.Id;

        if (entry.Entity.OwnerId == Guid.Empty)
        {
            SetValue(entry, nameof(IOwnedEntity.OwnerId),
                userId ?? throw new InvalidOperationException(
                    $"Cannot set OwnerId of {entry.Metadata.DisplayName()}: there is no current user."));
            return;
        }

        // Foydalanuvchi yo'q (ro'yxatdan o'tish, background job, seed) — OwnerId'ni chaqiruvchi qo'ygan, ruxsat.
        if (userId is null || !isolation.IsEnabled || isolation.Bypass)
            return;

        if (entry.Entity.OwnerId != userId)
            throw new ForbiddenOwnershipException($"Cannot create {entry.Metadata.DisplayName()} on behalf of another user.");
    }
}

/// <summary>
/// Deleted holatidagi ISoftDeletable'ni Modified + IsDeleted=true ga aylantiradi. Domain'da MarkDeleted()
/// chaqirilgan (IsDeleted false→true) entity'lar uchun ham DeletedAt/DeletedBy shu yerda to'ldiriladi.
/// </summary>
internal sealed class SoftDeleteInterceptor(ICurrentUser currentUser, TimeProvider timeProvider)
    : SavingChangesInterceptorBase
{
    protected override void Apply(DbContext context)
    {
        var now = timeProvider.GetUtcNow();

        foreach (var entry in context.ChangeTracker.Entries<ISoftDeletable>().ToList())
        {
            if (entry.State == EntityState.Deleted)
            {
                entry.State = EntityState.Modified;
                SetValue(entry, nameof(ISoftDeletable.IsDeleted), true);
                SetValue(entry, nameof(ISoftDeletable.DeletedAt), now);
                SetValue(entry, nameof(ISoftDeletable.DeletedBy), currentUser.Id);
                RestoreOwnedDependents(entry);
            }
            else if (entry.State == EntityState.Modified && entry.Property(nameof(ISoftDeletable.IsDeleted)).IsModified
                     && entry.Entity is { IsDeleted: true, DeletedAt: null })
            {
                SetValue(entry, nameof(ISoftDeletable.DeletedAt), now);
                SetValue(entry, nameof(ISoftDeletable.DeletedBy), currentUser.Id);
            }
        }
    }

    // Owned type'lar (OwnsOne/OwnsMany) egasi bilan birga Deleted bo'ladi — soft delete'da ularni saqlab qolamiz.
    private static void RestoreOwnedDependents(EntityEntry entry)
    {
        foreach (var navigation in entry.Navigations)
        {
            if (navigation.Metadata is not INavigation { TargetEntityType: var target } || !target.IsOwned())
                continue;

            var dependents = navigation switch
            {
                ReferenceEntry { TargetEntry: { } targetEntry } => [targetEntry],
                CollectionEntry collection when collection.CurrentValue is not null =>
                    collection.CurrentValue.Cast<object>().Select(collection.FindEntry).OfType<EntityEntry>().ToList(),
                _ => (List<EntityEntry>)[]
            };

            foreach (var dependent in dependents.Where(d => d.State == EntityState.Deleted))
            {
                dependent.State = EntityState.Unchanged;
                RestoreOwnedDependents(dependent);
            }
        }
    }
}

/// <summary>CreatedAt/CreatedBy va UpdatedAt/UpdatedBy'ni to'ldiradi (setter'lar private).</summary>
internal sealed class AuditingInterceptor(ICurrentUser currentUser, TimeProvider timeProvider)
    : SavingChangesInterceptorBase
{
    protected override void Apply(DbContext context)
    {
        var now = timeProvider.GetUtcNow();
        var userId = currentUser.Id;

        foreach (var entry in context.ChangeTracker.Entries<IAuditable>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    SetValue(entry, nameof(IAuditable.CreatedAt), now);
                    SetValue(entry, nameof(IAuditable.CreatedBy), userId);
                    break;
                case EntityState.Modified:
                    SetValue(entry, nameof(IAuditable.UpdatedAt), now);
                    SetValue(entry, nameof(IAuditable.UpdatedBy), userId);
                    // Yaratilish ma'lumotlari tasodifan qayta yozilmasin.
                    entry.Property(nameof(IAuditable.CreatedAt)).IsModified = false;
                    entry.Property(nameof(IAuditable.CreatedBy)).IsModified = false;
                    break;
            }
        }
    }
}

/// <summary>
/// Muvaffaqiyatli SaveChanges'dan keyin domain event'larni yig'adi, tozalaydi va dispatch qiladi.
/// Eslatma: ExecuteInTransactionAsync ichida event'lar commit'dan oldin (har SaveChanges'dan keyin) ishlaydi.
/// </summary>
internal sealed class DomainEventsInterceptor(IDomainEventDispatcher dispatcher) : SaveChangesInterceptor
{
    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        if (eventData.Context is not null)
            DispatchAsync(eventData.Context, CancellationToken.None).GetAwaiter().GetResult();
        return result;
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
            await DispatchAsync(eventData.Context, cancellationToken);
        return result;
    }

    private async Task DispatchAsync(DbContext context, CancellationToken cancellationToken)
    {
        var entities = context.ChangeTracker.Entries<IHasDomainEvents>()
            .Select(e => e.Entity)
            .Where(e => e.DomainEvents.Count > 0)
            .ToList();

        if (entities.Count == 0)
            return;

        var events = entities.SelectMany(e => e.DomainEvents).ToList();
        entities.ForEach(e => e.ClearDomainEvents());

        await dispatcher.DispatchAsync(events, cancellationToken);
    }
}
