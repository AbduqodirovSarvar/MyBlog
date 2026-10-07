using Microsoft.EntityFrameworkCore;
using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Domain.Common;
using MyBlog.Infrastructure.DataIsolation;
using MyBlog.Infrastructure.Persistence;
using MyBlog.Infrastructure.Persistence.Interceptors;
using NSubstitute;

namespace MyBlog.Api.IntegrationTests.Persistence;

/// <summary>Test entity: egasi bor va soft delete qilinadi (haqiqiy domain entity'lariga bog'liq emas).</summary>
internal sealed class Note : SoftDeletableEntity, IOwnedEntity, IAggregateRoot
{
    private Note() { }

    public Note(string title, Guid ownerId = default)
    {
        Title = title;
        OwnerId = ownerId;
    }

    public Guid OwnerId { get; private set; }
    public string Title { get; private set; } = string.Empty;

    public void Rename(string title) => Title = title;

    public void ChangeOwner(Guid ownerId) => OwnerId = ownerId;
}

internal sealed class TestIsolation : IDataIsolationContext
{
    public bool IsEnabled { get; set; } = true;
    public bool Bypass { get; set; }
    public Guid? CurrentUserId { get; set; }
}

/// <summary>
/// AppDbContext bilan bir xil filtr qurilishi (ApplyDataFilters) va interceptor'lar ishlatiladi, faqat model test entity'dan iborat.
/// </summary>
internal sealed class DataFilterTestContext(DbContextOptions<DataFilterTestContext> options, IDataIsolationContext isolation)
    : DbContext(options), IDataFilterSource
{
    public bool IsolationEnabled => isolation.IsEnabled;
    public bool BypassIsolation => isolation.Bypass;
    public Guid CurrentUserId => isolation.CurrentUserId ?? Guid.Empty;

    public DbSet<Note> Notes => Set<Note>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Note>(b =>
        {
            b.ToTable("notes");
            b.HasKey(n => n.Id);
            b.Property(n => n.Title).HasMaxLength(200);
        });

        modelBuilder.ApplyDataFilters(this);
    }

    /// <summary>Isolation holati va joriy foydalanuvchi bitta <see cref="TestIsolation"/> orqali boshqariladi.</summary>
    public static DataFilterTestContext Create(string connectionString, TestIsolation isolation)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.Id.Returns(_ => isolation.CurrentUserId);

        var options = new DbContextOptionsBuilder<DataFilterTestContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(
                new OwnershipInterceptor(currentUser, isolation),
                new SoftDeleteInterceptor(currentUser, TimeProvider.System),
                new AuditingInterceptor(currentUser, TimeProvider.System))
            .Options;

        return new DataFilterTestContext(options, isolation);
    }
}

internal sealed class NotesByTitleSpec : Specification<Note>
{
    public NotesByTitleSpec(bool ignoreOwnership = false, bool includeDeleted = false, int? page = null, int pageSize = 2)
    {
        OrderByAsc(n => n.Title);
        ReadOnly();
        if (ignoreOwnership)
            IgnoreOwnership();
        if (includeDeleted)
            IncludeDeleted();
        if (page is not null)
            Paginate(page.Value, pageSize);
    }
}

internal sealed class NoteTitlesSpec : Specification<Note, string>
{
    public NoteTitlesSpec(string startsWith)
    {
        Where(n => n.Title.StartsWith(startsWith));
        OrderByDesc(n => n.Title);
        IgnoreOwnership();
        Select(n => n.Title);
    }
}
