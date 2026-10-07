using System.Reflection;
using Microsoft.EntityFrameworkCore;
using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Application.Features.Media;
using MyBlog.Application.Features.Posts;
using MyBlog.Application.Features.Posts.Abstractions;
using MyBlog.Domain.Media;
using MyBlog.Domain.Posts;
using MyBlog.Domain.Users;
using MyBlog.Infrastructure.DataIsolation;
using MyBlog.Infrastructure.Persistence;
using MyBlog.Infrastructure.Persistence.Repositories;

namespace MyBlog.Infrastructure.Tests.Persistence;

/// <summary>
/// Repository va specification so'rovlari PostgreSQL SQL'iga tarjima bo'lishini bazasiz tekshiradi (ToQueryString).
/// Haqiqiy bajarilish integration testlarda (Docker bilan).
/// </summary>
public sealed class PostAndMediaQueryTranslationTests
{
    private static AppDbContext CreateContext() =>
        new(AppDbContextFactory.CreateDesignTimeOptions("Host=localhost;Database=model_only"), DisabledDataIsolationContext.Instance);

    [Fact]
    public void Unreferenced_media_query_checks_every_reference_source()
    {
        using var context = CreateContext();
        var sql = new MediaRepository(context).UnreferencedQuery(DateTimeOffset.UtcNow, 10).ToQueryString();

        sql.ShouldContain("post_media");
        sql.ShouldContain("cover_media_id");
        sql.ShouldContain("seo_og_image_media_id");
        sql.ShouldContain("avatar_media_id");
        sql.ShouldContain("icon_media_id");
        sql.ShouldContain("certificates");
    }

    [Fact]
    public void Public_list_uses_full_text_search_and_rank()
    {
        using var context = CreateContext();
        var repository = new PostRepository(context);
        var filter = new PublicPostFilter(Guid.NewGuid(), [Guid.NewGuid()], [Guid.NewGuid()], "salom dunyo", true, PublicPostSort.Relevance);

        var sql = PostRepository.PagePublished(repository.FilterPublished(filter), filter, 2, 10).ToQueryString();

        sql.ShouldContain("plainto_tsquery('simple'");
        sql.ShouldContain("@@");
        sql.ShouldContain("ts_rank");
        sql.ShouldContain("post_tags");

        foreach (var sort in Enum.GetValues<PublicPostSort>())
            Should.NotThrow(() => PostRepository.PagePublished(repository.FilterPublished(filter with { Search = null }),
                filter with { Sort = sort, Search = null }, 1, 10).ToQueryString());
    }

    [Fact]
    public void Media_specifications_translate()
    {
        using var context = CreateContext();
        var ids = new[] { Guid.NewGuid() };

        Should.NotThrow(() => Translate(context, new MediaByIdsForOwnerSpec(Guid.NewGuid(), ids)));
        Should.NotThrow(() => Translate(context, new MediaByIdsSpec(ids)));
        Should.NotThrow(() => Translate(context, CreateInternal<ISpecification<MediaFile>>(typeof(MediaDto),
            "MyBlog.Application.Features.Media.MyMediaPageSpec", "image", 2, 10, false)));
    }

    [Theory]
    [InlineData(MyPostSort.Updated)]
    [InlineData(MyPostSort.Created)]
    [InlineData(MyPostSort.Published)]
    [InlineData(MyPostSort.Title)]
    public void Post_specifications_translate(MyPostSort sort)
    {
        using var context = CreateContext();
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        TranslateProjection<Post>(context, "MyPostsSpec", PostStatus.Draft, id, id, "salom", sort, true, 1, 20, false);
        TranslateProjection<Post>(context, "PostSlugsSpec", id, "salom", (Guid?)id);
        TranslateProjection<Post>(context, "PublishedPostIdSpec", id, "salom");
        TranslateProjection<Post>(context, "AdjacentPostSpec", id, id, now, true);
        TranslateProjection<Post>(context, "AdjacentPostSpec", id, id, now, false);
        TranslateProjection<UserProfile>(context, "AuthorIdByUsernameSpec", "Ali");
        TranslateProjection<UserProfile>(context, "AuthorsByIdsSpec", (IReadOnlyCollection<Guid>)[id]);
        TranslateProjection<PostRevision>(context, "RevisionHeadersSpec", id, (RevisionKind?)RevisionKind.Autosave);
        TranslateProjection<Domain.Categories.Category>(context, "CategoryNodesBySlugSpec", "dasturlash", (Guid?)id);
        TranslateProjection<Domain.Tags.Tag>(context, "TagIdsBySlugSpec", "dotnet", (Guid?)null);
        TranslateProjection<Domain.Reactions.Reaction>(context, "UserReactionSpec", id, id);
        Translate(context, CreateInternal<ISpecification<Post>>(typeof(PostDto),
            "MyBlog.Application.Features.Posts.DueScheduledPostsSpec", now, 100));
    }

    private static string Translate<T>(AppDbContext context, ISpecification<T> specification) where T : class =>
        SpecificationEvaluator.Apply(context.Set<T>(), specification).ToQueryString();

    /// <summary>Application'dagi internal Specification&lt;T, TResult&gt;'ni reflection bilan yaratib, projection'ni tarjima qiladi.</summary>
    private static void TranslateProjection<T>(AppDbContext context, string specName, params object?[] args) where T : class
    {
        var spec = CreateInternal<ISpecification<T>>(typeof(PostDto), $"MyBlog.Application.Features.Posts.{specName}", args);
        var resultType = spec.GetType().BaseType!.GetGenericArguments()[1];

        var method = typeof(PostAndMediaQueryTranslationTests)
            .GetMethod(nameof(TranslateTyped), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(typeof(T), resultType);

        Should.NotThrow(() => method.Invoke(null, [context, spec]), specName);
    }

    private static string TranslateTyped<T, TResult>(AppDbContext context, ISpecification<T, TResult> specification) where T : class =>
        SpecificationEvaluator.Apply(context.Set<T>(), specification).ToQueryString();

    private static TSpec CreateInternal<TSpec>(Type assemblyMarker, string typeName, params object?[] args)
    {
        var type = assemblyMarker.Assembly.GetType(typeName, throwOnError: true)!;
        return (TSpec)Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, args, null)!;
    }
}
