using Microsoft.Extensions.Options;
using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Application.Features.Posts.Abstractions;
using MyBlog.Application.Features.Posts.Common;
using MyBlog.Application.Features.Posts.Content;
using MyBlog.Domain.Common;
using MyBlog.Domain.Media;
using MyBlog.Domain.Posts;
using MyBlog.Domain.Tags;

namespace MyBlog.Application.Features.Posts.Manage;

// ---------- Ro'yxat ----------

public sealed record ListPostRevisionsQuery(Guid PostId) : IQuery<IReadOnlyList<PostRevisionListItemDto>>;

internal sealed class ListPostRevisionsQueryHandler(IReadRepository<Post> posts, IReadRepository<PostRevision> revisions)
    : IQueryHandler<ListPostRevisionsQuery, IReadOnlyList<PostRevisionListItemDto>>
{
    public async Task<Result<IReadOnlyList<PostRevisionListItemDto>>> Handle(ListPostRevisionsQuery request,
        CancellationToken cancellationToken)
    {
        if (!await posts.AnyAsync(new MyPostByIdSpec(request.PostId, tracked: false), cancellationToken))
            return PostErrors.NotFound;

        var headers = await revisions.ListAsync(new RevisionHeadersSpec(request.PostId), cancellationToken);
        return headers
            .OrderByDescending(h => h.Number)
            .Select(h => new PostRevisionListItemDto(h.Id, h.Number, h.Kind, h.Title, h.CreatedAt))
            .ToList();
    }
}

// ---------- Bitta reviziya ----------

public sealed record GetPostRevisionQuery(Guid PostId, Guid RevisionId) : IQuery<PostRevisionDto>;

internal sealed class GetPostRevisionQueryHandler(IReadRepository<PostRevision> revisions)
    : IQueryHandler<GetPostRevisionQuery, PostRevisionDto>
{
    public async Task<Result<PostRevisionDto>> Handle(GetPostRevisionQuery request, CancellationToken cancellationToken)
    {
        // Reviziya ham IOwnedEntity — boshqa foydalanuvchiniki topilmaydi.
        var revision = await revisions.FirstOrDefaultAsync(new RevisionByIdSpec(request.PostId, request.RevisionId), cancellationToken);
        if (revision is null)
            return PostErrors.RevisionNotFound;

        return new PostRevisionDto(revision.Id, revision.PostId, revision.RevisionNumber, revision.Kind, revision.Title,
            PostDtoBuilder.ToContentDto(revision.Content), revision.CreatedAt);
    }
}

// ---------- Tiklash ----------

/// <summary>
/// Joriy holat Manual reviziya sifatida saqlanadi, so'ng tanlangan reviziyaning sarlavha va kontenti qo'llanadi.
/// Kontent pipeline'dan qayta o'tkaziladi (media hali mavjud va muallifniki ekanligi tekshiriladi).
/// </summary>
public sealed record RestorePostRevisionCommand(Guid PostId, Guid RevisionId) : ICommand<PostDto>;

internal sealed class RestorePostRevisionCommandHandler(
    IRepository<Post> posts,
    IRepository<PostRevision> revisions,
    IPostRepository postRepository,
    IReadRepository<Tag> tags,
    IReadRepository<MediaFile> media,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IContentProcessorFactory contentProcessors,
    IFileStorage storage,
    ICacheService cache,
    IOptions<PostsOptions> options,
    TimeProvider timeProvider)
    : ICommandHandler<RestorePostRevisionCommand, PostDto>
{
    public async Task<Result<PostDto>> Handle(RestorePostRevisionCommand request, CancellationToken cancellationToken)
    {
        var ownerId = currentUser.RequiredId;

        var post = await posts.FirstOrDefaultAsync(new MyPostByIdSpec(request.PostId), cancellationToken);
        if (post is null)
            return PostErrors.NotFound;

        var revision = await revisions.FirstOrDefaultAsync(new RevisionByIdSpec(post.Id, request.RevisionId), cancellationToken);
        if (revision is null)
            return PostErrors.RevisionNotFound;

        var input = new ContentInput(revision.Content.Format, revision.Content.Html, revision.Content.Raw);
        var content = await PostEditing.ProcessContentAsync(contentProcessors, input, ownerId, cancellationToken);
        if (content.IsFailure)
            return content.Error;

        var now = timeProvider.GetUtcNow();
        var saved = await unitOfWork.ExecuteInTransactionAsync<Result>(async ct =>
        {
            await PostRevisionWriter.AddAsync(revisions, postRepository, options.Value, post, RevisionKind.Manual, now, ct,
                clearAutosaves: revision.Kind == RevisionKind.Autosave);

            var details = post.UpdateDetails(revision.Title, post.Slug, post.Summary, post.CategoryId, post.CoverMediaId, post.Seo);
            if (details.IsFailure)
                return details;

            var contentResult = post.UpdateContent(content.Value.Content, content.Value.ReadingTimeMinutes, content.Value.MediaIds);
            if (contentResult.IsFailure)
                return contentResult;

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
