using FluentValidation;
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

/// <param name="Slug">Berilmasa sarlavhadan yaratiladi (muallif bo'yicha unikal).</param>
/// <param name="Tags">Teg nomlari (mavjud bo'lmaganlari Tags moduli tomonidan yaratiladi).</param>
public sealed record CreatePostCommand(
    string Title,
    string? Slug,
    string? Summary,
    Guid? CategoryId,
    IReadOnlyList<string>? Tags,
    Guid? CoverMediaId,
    ContentInput Content,
    bool AllowComments = true,
    PostSeoInput? Seo = null) : ICommand<PostDto>;

internal sealed class CreatePostCommandValidator : AbstractValidator<CreatePostCommand>
{
    public CreatePostCommandValidator()
    {
        RuleFor(x => x.Title).NotEmpty().WithErrorCode(PostErrors.TitleRequired.Code).WithMessage(PostErrors.TitleRequired.Description);
        RuleFor(x => x.Content).NotNull().WithErrorCode(PostErrors.ContentFormatRequired.Code).WithMessage(PostErrors.ContentFormatRequired.Description);
        RuleFor(x => x.Content.Format).NotEmpty().When(x => x.Content is not null)
            .WithErrorCode(PostErrors.ContentFormatRequired.Code).WithMessage(PostErrors.ContentFormatRequired.Description);
    }
}

internal sealed class CreatePostCommandHandler(
    IRepository<Post> posts,
    IPostRepository postRepository,
    IReadRepository<Category> categories,
    IReadRepository<Tag> tags,
    IReadRepository<MediaFile> media,
    IReadRepository<PostRevision> revisions,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ISlugGenerator slugGenerator,
    IContentProcessorFactory contentProcessors,
    ITagResolver tagResolver,
    IFileStorage storage)
    : ICommandHandler<CreatePostCommand, PostDto>
{
    public async Task<Result<PostDto>> Handle(CreatePostCommand request, CancellationToken cancellationToken)
    {
        var ownerId = currentUser.RequiredId;

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

        var slug = await PostEditing.ResolveSlugAsync(posts, slugGenerator, ownerId, request.Slug, request.Title, null, cancellationToken);
        if (slug.IsFailure)
            return slug.Error;

        var content = await PostEditing.ProcessContentAsync(contentProcessors, request.Content, ownerId, cancellationToken);
        if (content.IsFailure)
            return content.Error;

        var created = Post.Create(ownerId, request.Title, slug.Value, content.Value.Content, content.Value.ReadingTimeMinutes,
            request.Summary, request.CategoryId, request.CoverMediaId, seo.Value);
        if (created.IsFailure)
            return created.Error;

        var post = created.Value;
        var contentResult = post.UpdateContent(content.Value.Content, content.Value.ReadingTimeMinutes, content.Value.MediaIds);
        if (contentResult.IsFailure)
            return contentResult.Error;
        post.SetAllowComments(request.AllowComments);

        // Teglar yaratilishi va post bitta tranzaksiyada.
        var saved = await unitOfWork.ExecuteInTransactionAsync<Result>(async ct =>
        {
            if (tagNames.Count > 0)
            {
                var tagResult = post.SetTags(await tagResolver.ResolveAsync(ownerId, tagNames, ct));
                if (tagResult.IsFailure)
                    return tagResult;
            }

            posts.Add(post);
            await unitOfWork.SaveChangesAsync(ct);
            return Result.Success();
        }, cancellationToken);

        if (saved.IsFailure)
            return saved.Error;

        return await PostDtoBuilder.BuildAsync(post, postRepository.GetVersion(post),
            new PostDtoSources(tags, media, revisions, storage), cancellationToken);
    }
}

internal static class TagNames
{
    /// <summary>Bo'sh qiymatlarsiz, trim qilingan, katta-kichik harfga befarq takrorlarsiz.</summary>
    public static IReadOnlyList<string> Normalize(IEnumerable<string>? names) =>
        (names ?? [])
            .Select(DomainRules.TrimToNull)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
