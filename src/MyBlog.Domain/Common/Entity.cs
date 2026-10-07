namespace MyBlog.Domain.Common;

public interface IDomainEvent;

public interface IHasDomainEvents
{
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }
    void ClearDomainEvents();
}

public abstract class Entity<TId> : IHasDomainEvents where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected Entity() { }
    protected Entity(TId id) => Id = id;

    public TId Id { get; protected set; } = default!;

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}

/// <summary>Guid Id'li entity. Id'lar vaqt bo'yicha tartiblangan (Guid v7) bo'ladi.</summary>
public abstract class Entity : Entity<Guid>
{
    protected Entity() : base(Guid.CreateVersion7()) { }
    protected Entity(Guid id) : base(id) { }
}

public interface IAggregateRoot;

public interface IAuditable
{
    DateTimeOffset CreatedAt { get; }
    Guid? CreatedBy { get; }
    DateTimeOffset? UpdatedAt { get; }
    Guid? UpdatedBy { get; }
}

public interface ISoftDeletable
{
    bool IsDeleted { get; }
    DateTimeOffset? DeletedAt { get; }
    Guid? DeletedBy { get; }
}

/// <summary>
/// Egasi bor entity. Ownership query filter va OwnerId interceptor shu interfeys orqali ishlaydi.
/// OwnerId'ni infrastruktura avtomatik qo'yadi; keyin uni o'zgartirib bo'lmaydi.
/// </summary>
public interface IOwnedEntity
{
    Guid OwnerId { get; }
}

/// <summary>
/// Audit maydonlari infrastruktura (SaveChanges interceptor) tomonidan to'ldiriladi,
/// shuning uchun setter'lar private va EF ularni property access orqali yozadi.
/// </summary>
public abstract class AuditableEntity : Entity, IAuditable
{
    protected AuditableEntity() { }
    protected AuditableEntity(Guid id) : base(id) { }

    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    public Guid? UpdatedBy { get; private set; }
}

public abstract class SoftDeletableEntity : AuditableEntity, ISoftDeletable
{
    protected SoftDeletableEntity() { }
    protected SoftDeletableEntity(Guid id) : base(id) { }

    public bool IsDeleted { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public Guid? DeletedBy { get; private set; }

    /// <summary>Soft delete. Vaqt va kim o'chirgani interceptor tomonidan to'ldiriladi.</summary>
    public virtual void MarkDeleted() => IsDeleted = true;

    public virtual void Restore()
    {
        IsDeleted = false;
        DeletedAt = null;
        DeletedBy = null;
    }
}
