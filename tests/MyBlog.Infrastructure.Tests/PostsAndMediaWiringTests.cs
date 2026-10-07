using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MyBlog.Application;
using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Application.Common.Models;
using MyBlog.Application.Features.Media;
using MyBlog.Application.Features.Media.Abstractions;
using MyBlog.Application.Features.Media.Upload;
using MyBlog.Application.Features.Posts;
using MyBlog.Application.Features.Posts.Abstractions;
using MyBlog.Application.Features.Posts.Content;
using MyBlog.Application.Features.Posts.Manage;
using MyBlog.Application.Features.Posts.Public;
using MyBlog.Application.Features.Tags.Abstractions;
using MyBlog.Domain.Common;
using MyBlog.Domain.Media;
using MyBlog.Domain.Posts;
using MyBlog.Domain.Reactions;
using MyBlog.Infrastructure.Content.Processing;
using MyBlog.Infrastructure.Localization;
using MyBlog.Infrastructure.Media;
using MyBlog.Infrastructure.Persistence.Repositories;
using NSubstitute;

namespace MyBlog.Infrastructure.Tests;

public sealed class PostsAndMediaWiringTests
{
    private static ServiceProvider BuildProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "Host=localhost;Database=di_only",
                ["Smtp:Host"] = "localhost",
                ["Smtp:FromAddress"] = "no-reply@myblog.local",
                ["Media:MaxUploadBytes"] = "1234",
                ["Media:Variants:thumb"] = "320",
                ["Posts:MaxRevisions"] = "7",
                ["Content:RawDocumentFormats:0"] = "tiptap-json"
            })
            .Build();
        var environment = Substitute.For<IHostEnvironment>();
        environment.ContentRootPath.Returns(Path.GetTempPath());
        environment.EnvironmentName.Returns(Environments.Development);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(environment);
        services.AddScoped(_ => Substitute.For<ICurrentUser>());
        // Tags moduli (boshqa branch) implementatsiya qiladi.
        services.AddScoped(_ => Substitute.For<ITagResolver>());
        services.AddApplication();
        services.AddInfrastructure(configuration, environment);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    [Fact]
    public async Task Media_and_posts_services_are_wired()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        sp.GetRequiredService<IImageProcessor>().ShouldBeOfType<SkiaImageProcessor>();
        sp.GetRequiredService<IFileSignatureValidator>().ShouldBeOfType<MagicBytesSignatureValidator>();
        sp.GetRequiredService<IMediaRepository>().ShouldBeOfType<MediaRepository>();
        sp.GetRequiredService<IPostRepository>().ShouldBeOfType<PostRepository>();
        sp.GetRequiredService<IOptions<MediaOptions>>().Value.MaxUploadBytes.ShouldBe(1234);
        sp.GetRequiredService<IOptions<PostsOptions>>().Value.MaxRevisions.ShouldBe(7);

        var factory = sp.GetRequiredService<IContentProcessorFactory>();
        factory.Get("html").Value.ShouldBeOfType<HtmlContentProcessor>();
        factory.Get("tiptap-json").Value.ShouldBeOfType<HtmlWithRawDocumentContentProcessor>();
        factory.Get("quill-delta").IsFailure.ShouldBeTrue(); // konfiguratsiyada faqat tiptap-json

        // Handler'lar butun bog'liqlik grafi bilan yaratiladi.
        sp.GetRequiredService<IRequestHandler<UploadMediaCommand, Result<MediaDto>>>().ShouldNotBeNull();
        sp.GetRequiredService<IRequestHandler<CreatePostCommand, Result<PostDto>>>().ShouldNotBeNull();
        sp.GetRequiredService<IRequestHandler<UpdatePostCommand, Result<PostDto>>>().ShouldNotBeNull();
        sp.GetRequiredService<IRequestHandler<GetPublicPostQuery, Result<PublicPostDetailDto>>>().ShouldNotBeNull();
        sp.GetRequiredService<IRequestHandler<ListPublicPostsQuery, Result<PagedList<PublicPostSummaryDto>>>>().ShouldNotBeNull();

        sp.GetServices<IRecurringJob>().Select(j => j.Name)
            .ShouldContain("media-orphan-cleanup");
        sp.GetServices<IRecurringJob>().Select(j => j.Name)
            .ShouldContain("posts-scheduled-publisher");
    }

    [Fact]
    public void All_media_and_post_error_codes_are_translated_in_every_culture()
    {
        var resources = JsonLocalizer.LoadEmbeddedResources(typeof(JsonLocalizer).Assembly);
        var codes = typeof(MediaErrors).GetFields().Concat(typeof(PostErrors).GetFields())
            .Where(f => f.FieldType == typeof(Error))
            .Select(f => ((Error)f.GetValue(null)!).Code)
            .ToList();

        codes.Count.ShouldBeGreaterThan(40);
        foreach (var culture in (string[])["uz", "uz-Cyrl", "ru", "en"])
        {
            var missing = codes.Where(c => !resources[culture].ContainsKey(c)).ToList();
            missing.ShouldBeEmpty($"culture {culture}");
        }
    }

    [Fact]
    public void Public_detail_dto_round_trips_through_json_with_string_enums()
    {
        var dto = new PublicPostDetailDto(Guid.NewGuid(), "T", "t", null, "<p>x</p>", [new TocItem(2, "a", "A")], null,
            DateTimeOffset.UnixEpoch, null, 1, true, false, new PublicPostSeoDto("T", null, null, null),
            new PublicAuthorDto("ali", "Ali", null), null, [], new PostCountsDto(1, 2, 3, 4), null, null, ReactionType.Like);

        var json = JsonSerializer.Serialize(dto, JsonSerializerOptions.Web);
        json.ShouldContain("\"myReaction\":\"Like\"");

        var back = JsonSerializer.Deserialize<PublicPostDetailDto>(json, JsonSerializerOptions.Web)!;
        back.MyReaction.ShouldBe(ReactionType.Like);
        back.Toc.ShouldHaveSingleItem().Id.ShouldBe("a");

        JsonSerializer.Serialize(dto with { MyReaction = null }, JsonSerializerOptions.Web).ShouldContain("\"myReaction\":null");

        var postDto = new PostDto(Guid.NewGuid(), "T", "t", null, PostStatus.Scheduled, null, [], null, null,
            new PostContentDto("tiptap-json", "<p/>", JsonDocument.Parse("{\"type\":\"doc\"}").RootElement.Clone()), [], 1, true,
            false, new PostSeoDto(null, null, null, null), DateTimeOffset.UnixEpoch, null, null, null, new PostCountsDto(0, 0, 0, 0),
            null, 42);
        var postJson = JsonSerializer.Serialize(postDto, JsonSerializerOptions.Web);
        postJson.ShouldContain("\"status\":\"Scheduled\"");
        postJson.ShouldContain("\"raw\":{\"type\":\"doc\"}");
        postJson.ShouldContain("\"version\":42");
    }
}
