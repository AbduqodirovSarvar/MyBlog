using System.Reflection;
using MyBlog.Application.Abstractions.Authorization;
using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Domain.Common;

namespace MyBlog.Application.Tests.Features;

/// <summary>Tranzaksiyasiz: operatsiyani darhol bajaradi va SaveChanges chaqiruvlarini sanaydi.</summary>
internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public int SaveChangesCalls { get; private set; }
    public int TransactionCalls { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveChangesCalls++;
        return Task.FromResult(1);
    }

    public Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        TransactionCalls++;
        return operation(cancellationToken);
    }
}

internal sealed class FakeCurrentUser(Guid? id, params string[] permissions) : ICurrentUser
{
    public Guid? Id { get; } = id;
    public bool IsAuthenticated => Id is not null;
    public string? UserName => null;
    public string? Email => null;
    public IReadOnlyCollection<string> Roles => [];

    public bool IsInRole(string role) => false;

    public bool HasPermission(string permission) => IsAuthenticated && permissions.Contains(permission);
}

/// <summary>
/// <see cref="IPublicContentPolicy"/> test varianti. <see cref="Open"/> — ochiq tizim (default sozlama);
/// <see cref="ClosedFor"/> — yopiq tizim, faqat berilgan foydalanuvchining kontenti (null — anonim).
/// </summary>
internal sealed class FakePublicContentPolicy(Guid? ownerScope) : IPublicContentPolicy
{
    public static readonly FakePublicContentPolicy Open = new(null);

    public static FakePublicContentPolicy ClosedFor(Guid? userId) => new(userId ?? Guid.Empty);

    public bool IsPublicReadEnabled => OwnerScope is null;

    public Guid? OwnerScope { get; } = ownerScope;
}

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

internal static class EntityTestExtensions
{
    /// <summary>CreatedAt'ni interceptor o'rniga qo'yish (private setter).</summary>
    public static T WithCreatedAt<T>(this T entity, DateTimeOffset createdAt) where T : AuditableEntity
    {
        typeof(AuditableEntity)
            .GetProperty(nameof(AuditableEntity.CreatedAt), BindingFlags.Public | BindingFlags.Instance)!
            .SetValue(entity, createdAt);
        return entity;
    }
}
