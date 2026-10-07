using System.Linq.Expressions;

namespace MyBlog.Application.Abstractions.Persistence;

public interface ISpecification<T> where T : class
{
    IReadOnlyList<Expression<Func<T, bool>>> Criteria { get; }
    IReadOnlyList<Expression<Func<T, object>>> Includes { get; }
    IReadOnlyList<string> IncludeStrings { get; }
    IReadOnlyList<(Expression<Func<T, object>> KeySelector, bool Descending)> OrderBy { get; }
    int? Skip { get; }
    int? Take { get; }
    bool AsNoTracking { get; }
    bool AsSplitQuery { get; }

    /// <summary>Ownership filtrini o'chiradi (public o'qish va background job'lar uchun).</summary>
    bool IgnoreOwnershipFilter { get; }

    /// <summary>Soft delete filtrini o'chiradi (o'chirilganlarni ham olish).</summary>
    bool IgnoreSoftDeleteFilter { get; }
}

public interface ISpecification<T, TResult> : ISpecification<T> where T : class
{
    Expression<Func<T, TResult>> Selector { get; }
}

/// <summary>
/// So'rovni Application qatlamida EF Core'ga bog'lanmasdan tasvirlash uchun.
/// Infrastructure'dagi SpecificationEvaluator uni IQueryable'ga aylantiradi.
/// </summary>
public abstract class Specification<T> : ISpecification<T> where T : class
{
    private readonly List<Expression<Func<T, bool>>> _criteria = [];
    private readonly List<Expression<Func<T, object>>> _includes = [];
    private readonly List<string> _includeStrings = [];
    private readonly List<(Expression<Func<T, object>>, bool)> _orderBy = [];

    public IReadOnlyList<Expression<Func<T, bool>>> Criteria => _criteria;
    public IReadOnlyList<Expression<Func<T, object>>> Includes => _includes;
    public IReadOnlyList<string> IncludeStrings => _includeStrings;
    public IReadOnlyList<(Expression<Func<T, object>> KeySelector, bool Descending)> OrderBy => _orderBy;
    public int? Skip { get; private set; }
    public int? Take { get; private set; }
    public bool AsNoTracking { get; private set; }
    public bool AsSplitQuery { get; private set; }
    public bool IgnoreOwnershipFilter { get; private set; }
    public bool IgnoreSoftDeleteFilter { get; private set; }

    protected void Where(Expression<Func<T, bool>> criteria) => _criteria.Add(criteria);
    protected void Include(Expression<Func<T, object>> include) => _includes.Add(include);
    protected void Include(string includePath) => _includeStrings.Add(includePath);
    protected void OrderByAsc(Expression<Func<T, object>> keySelector) => _orderBy.Add((keySelector, false));
    protected void OrderByDesc(Expression<Func<T, object>> keySelector) => _orderBy.Add((keySelector, true));
    protected void ReadOnly() => AsNoTracking = true;
    protected void SplitQuery() => AsSplitQuery = true;
    protected void IgnoreOwnership() => IgnoreOwnershipFilter = true;
    protected void IncludeDeleted() => IgnoreSoftDeleteFilter = true;

    protected void Paginate(int page, int pageSize)
    {
        Skip = (Math.Max(page, 1) - 1) * pageSize;
        Take = pageSize;
    }
}

public abstract class Specification<T, TResult> : Specification<T>, ISpecification<T, TResult> where T : class
{
    public Expression<Func<T, TResult>> Selector { get; private set; } = null!;

    protected void Select(Expression<Func<T, TResult>> selector) => Selector = selector;
}
