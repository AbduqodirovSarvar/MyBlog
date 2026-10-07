using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using MyBlog.Domain.Common;

namespace MyBlog.Infrastructure.Persistence;

/// <summary>EF Core 10 nomlangan query filter kalitlari (IgnoreQueryFilters([..]) uchun).</summary>
internal static class QueryFilterNames
{
    public const string Ownership = "Ownership";
    public const string SoftDelete = "SoftDelete";
}

/// <summary>Ownership filtri o'qiydigan DbContext xususiyatlari.</summary>
internal interface IDataFilterSource
{
    bool IsolationEnabled { get; }
    bool BypassIsolation { get; }
    Guid CurrentUserId { get; }
}

internal static class QueryFilterModelBuilderExtensions
{
    /// <summary>
    /// Barcha root entity'larga Ownership (IOwnedEntity) va SoftDelete (ISoftDeletable) filtrlarini qo'yadi.
    /// Filtr DbContext instance property'lariga murojaat qiladi — EF ularni har so'rovda parametr sifatida oladi.
    /// </summary>
    public static ModelBuilder ApplyDataFilters<TContext>(this ModelBuilder modelBuilder, TContext context)
        where TContext : DbContext, IDataFilterSource
    {
        var contextExpression = Expression.Constant(context, typeof(TContext));

        var rootTypes = modelBuilder.Model.GetEntityTypes()
            .Where(t => t.BaseType is null && !t.IsOwned() && !t.HasSharedClrType)
            .ToList();

        foreach (var entityType in rootTypes)
        {
            var clrType = entityType.ClrType;
            var builder = modelBuilder.Entity(clrType);

            if (typeof(IOwnedEntity).IsAssignableFrom(clrType))
            {
                // e => !ctx.IsolationEnabled || ctx.BypassIsolation || e.OwnerId == ctx.CurrentUserId
                var e = Expression.Parameter(clrType, "e");
                var body = Expression.OrElse(
                    Expression.Not(Expression.Property(contextExpression, nameof(IDataFilterSource.IsolationEnabled))),
                    Expression.OrElse(
                        Expression.Property(contextExpression, nameof(IDataFilterSource.BypassIsolation)),
                        Expression.Equal(
                            OwnerIdAccess(e, clrType),
                            Expression.Property(contextExpression, nameof(IDataFilterSource.CurrentUserId)))));

                builder.HasQueryFilter(QueryFilterNames.Ownership, Expression.Lambda(body, e));
            }

            if (typeof(ISoftDeletable).IsAssignableFrom(clrType))
            {
                // e => !e.IsDeleted
                var e = Expression.Parameter(clrType, "e");
                var body = Expression.Not(MemberAccess(e, clrType, typeof(ISoftDeletable), nameof(ISoftDeletable.IsDeleted)));
                builder.HasQueryFilter(QueryFilterNames.SoftDelete, Expression.Lambda(body, e));
            }
        }

        return modelBuilder;
    }

    private static Expression OwnerIdAccess(ParameterExpression e, Type clrType) =>
        MemberAccess(e, clrType, typeof(IOwnedEntity), nameof(IOwnedEntity.OwnerId));

    // Public property bo'lsa to'g'ridan-to'g'ri, explicit implementatsiya bo'lsa interfeys orqali.
    private static Expression MemberAccess(ParameterExpression e, Type clrType, Type @interface, string name) =>
        clrType.GetProperty(name) is { } property
            ? Expression.Property(e, property)
            : Expression.Property(Expression.Convert(e, @interface), @interface.GetProperty(name)!);
}
