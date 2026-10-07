using Microsoft.EntityFrameworkCore;
using MyBlog.Application.Abstractions.Persistence;

namespace MyBlog.Infrastructure.Persistence.Repositories;

/// <summary>ISpecification'ni IQueryable'ga aylantiradi.</summary>
internal static class SpecificationEvaluator
{
    /// <param name="forCount">Count/Any uchun: include, tartiblash va sahifalash qo'llanmaydi.</param>
    public static IQueryable<T> Apply<T>(IQueryable<T> source, ISpecification<T> specification, bool forCount = false)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(specification);

        var query = ApplyFilterOptions(source, specification);

        foreach (var criteria in specification.Criteria)
            query = query.Where(criteria);

        if (forCount)
            return query;

        foreach (var include in specification.Includes)
            query = query.Include(include);

        foreach (var includePath in specification.IncludeStrings)
            query = query.Include(includePath);

        if (specification.OrderBy.Count > 0)
        {
            var (firstKey, firstDescending) = specification.OrderBy[0];
            var ordered = firstDescending ? query.OrderByDescending(firstKey) : query.OrderBy(firstKey);

            foreach (var (key, descending) in specification.OrderBy.Skip(1))
                ordered = descending ? ordered.ThenByDescending(key) : ordered.ThenBy(key);

            query = ordered;
        }

        if (specification.Skip is > 0)
            query = query.Skip(specification.Skip.Value);

        if (specification.Take is not null)
            query = query.Take(specification.Take.Value);

        if (specification.AsNoTracking)
            query = query.AsNoTracking();

        if (specification.AsSplitQuery)
            query = query.AsSplitQuery();

        return query;
    }

    public static IQueryable<TResult> Apply<T, TResult>(IQueryable<T> source, ISpecification<T, TResult> specification)
        where T : class
    {
        if (specification.Selector is null)
            throw new InvalidOperationException($"Specification {specification.GetType().Name} has no Select(...) projection.");

        return Apply(source, (ISpecification<T>)specification).Select(specification.Selector);
    }

    private static IQueryable<T> ApplyFilterOptions<T>(IQueryable<T> query, ISpecification<T> specification) where T : class
    {
        var ignored = new List<string>(2);
        if (specification.IgnoreOwnershipFilter)
            ignored.Add(QueryFilterNames.Ownership);
        if (specification.IgnoreSoftDeleteFilter)
            ignored.Add(QueryFilterNames.SoftDelete);

        return ignored.Count == 0 ? query : query.IgnoreQueryFilters(ignored);
    }
}
