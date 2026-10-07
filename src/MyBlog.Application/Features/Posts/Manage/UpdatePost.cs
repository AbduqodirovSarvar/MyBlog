using FluentValidation;
using Microsoft.Extensions.Options;
using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Application.Features.Posts.Abstractions;
using MyBlog.Application.Features.Posts.Common;
using MyBlog.Application.Features.Posts.Content;
using MyBlog.Application.Features.Tags.Abstractions;
using MyBlog.Domain.Categories;
using MyBlog.Domain.Common;
using MyBlog.Domain.Media;
using MyBlog.Domain.Posts;
using MyBlog.Domain.Tags;

namespace MyBlog.Application.Features.Posts.Manage;

/// <param name="Slug">null — joriy slug saqlanadi (URL barqarorligi uchun sarlavhadan qayta yaratilmaydi).</param>
/// <param name="Version">Mijoz ko'rgan versiya; mos kelmasa 409 (null — tekshirilmaydi).</param>
public sealed record UpdatePostCommand(
    Guid Id,
    string Title,
    string? Slug,
    string? Summary,
    Guid? CategoryId,
    IReadOnlyList<string>? Tags,
    Guid? CoverMediaId,
    ContentInput Content,
    bool AllowComments = true,
    PostSeoInput? Seo = null,
    uint? Version = null) : ICommand<PostDto>;

internal sealed class UpdatePostCommandValidator : AbstractValidator<UpdatePostCommand>
{
    public UpdatePostCommandValidator()
    {
        RuleFor(x => x.Title).NotEmpty().WithErrorCode(PostErrors.TitleRequired.Code).WithMessage(PostErrors.TitleRequired.Description);
        RuleFor(x => x.Content).NotNull().WithErrorCode(PostErrors.ContentFormatRequired.Code).WithMessage(PostErrors.ContentFormatRequired.Description);
        RuleFor(x => x.Content.Format).NotEmpty().When(x => x.Content is not null)
            .WithErrorCode(PostErrors.ContentFormatRequired.Code).WithMessage(PostErrors.ContentFormatRequired.Description);
    }
}

/// <summary>Sarlavha yoki kontent o'zgarsa oldingi holat Manual reviziya sifatida saqlanadi.</summary>
internal sealed class UpdatePostCommandHandler(
    IRepository<Post> posts,
    IPostRepository postRepository,
    IRepository<PostRevision> revisions,
    IReadRepository<Category> categories,
    IReadRepository<Tag> tags,
    IReadRepository<MediaFile> media,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ISlugGenerator slugGenerator,
    IContentProcessorFactory contentProcessors,
    ITagResolver tagResolver,
    IFileStorage storage,
    ICacheService cache,
    IOptions<PostsOptions> options,
    TimeProvider timeProvider)
    : ICommandHandler<UpdatePostCommand, PostDto>
{
    public async Task<Result<PostDto>> Handle(UpdatePostCommand request, CancellationToken cancellationToken)
    {
        var ownerId = currentUser.RequiredId;

        var post = await posts.FirstOrDefaultAsync(new MyPostByIdSpec(request.Id), cancellationToken);
        if (post is null)
            return PostErrors.NotFound;

        if (request.Version is { } expected)
        {
            if (postRepository.GetVersion(post) != expected)
                return PostErrors.VersionConflict;
            // Tekshiruv va UPDATE orasidagi poyga ham ushlansin (WHERE xmin = expected).
            postRepository.SetExpectedVersion(post, expected);
        }

        var tagNames = TagNames.Normalize(request.Tags);
        if (tagNames.Count > PostConstraints.MaxTags)
            return PostErrors.TooManyTags;

        var seo = PostEditing.CreateSeo(request.Seo);
        if (seo.IsFailure)
            return seo.Error;

        var references = await PostEditing.ValidateReferencesAsync(categories, media, ownerId, request.CategoryId,
            request.CoverMediaId, request.Seo?.OgImageMediaId, cancellationToken);
        if (references.IsFailure)
            return references.Error;

        var slug = request.Slug is null
            ? Result.Success(post.Slug)
            : await PostEditing.ResolveSlugAsync(posts, slugGenerator, ownerId, request.Slug, request.Title, post.Id, cancellationToken);
        if (slug.IsFailure)
            return slug.Error;

        var content = await PostEditing.ProcessContentAsync(contentProcessors, request.Content, ownerId, cancellationToken);
        if (content.IsFailure)
            return content.Error;

        var contentChanged = PostEditing.ContentDiffers(post, request.Title, content.Value.Content);
        var now = timeProvider.GetUtcNow();

        var saved = await unitOfWork.ExecuteInTransactionAsync<Result>(async ct =>
        {
            // Snapshot o'zgartirishdan OLDIN olinadi.
            if (contentChanged)
                await PostRevisionWriter.AddAsync(revisions, postRepository, options.Value, post, RevisionKind.Manual, now, ct,
                    clearAutosaves: true);

            var details = post.UpdateDetails(request.Title, slug.Value, request.Summary, request.CategoryId,
                request.CoverMediaId, seo.Value);
            if (details.IsFailure)
                return details;

            var contentResult = post.UpdateContent(content.Value.Content, content.Value.ReadingTimeMinutes, content.Value.MediaIds);
            if (contentResult.IsFailure)
                return contentResult;

            var tagIds = tagNames.Count == 0 ? [] : await tagResolver.ResolveAsync(ownerId, tagNames, ct);
            var tagResult = post.SetTags(tagIds);
            if (tagResult.IsFailure)
                return tagResult;

            post.SetAllowComments(request.AllowComments);
            await unitOfWork.SaveChangesAsync(ct);
            return Result.Success();
        }, cancellationToken);

        if (saved.IsFailure)
            return saved.Error;

        await cache.RemoveByTagAsync(PostCache.Tag(post.Id), cancellationToken);

        return await PostDtoBuilder.BuildAsync(post, postRepository.GetVersion(post),
            new PostDtoSources(tags, media, revisions, storage), cancellationToken);
    }
}
