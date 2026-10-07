using Microsoft.Extensions.Options;
using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Application.Features.Posts.Abstractions;
using MyBlog.Application.Features.Posts.Common;
using MyBlog.Application.Features.Profile.Common;
using MyBlog.Domain.Common;
using MyBlog.Domain.Media;
using MyBlog.Domain.Posts;
using MyBlog.Domain.Tags;

namespace MyBlog.Application.Features.Posts.Manage;

public enum PostStatusAction
{
    Publish,
    Unpublish,
    Schedule,
    Archive,
    Feature,
    Unfeature
}

/// <param name="ScheduledAt">Faqat <see cref="PostStatusAction.Schedule"/> uchun (kelajakdagi vaqt).</param>
public sealed record ChangePostStatusCommand(Guid Id, PostStatusAction Action, DateTimeOffset? ScheduledAt = null)
    : ICommand<PostDto>;

/// <summary>Holat o'tishlari domain'da tekshiriladi. Nashrda Publish reviziyasi yoziladi; public kesh tozalanadi.</summary>
internal sealed class ChangePostStatusCommandHandler(
    IRepository<Post> posts,
    IRepository<PostRevision> revisions,
    IPostRepository postRepository,
    IReadRepository<Tag> tags,
    IReadRepository<MediaFile> media,
    IUnitOfWork unitOfWork,
    IFileStorage storage,
    ICacheService cache,
    IAuthorCacheInvalidator authorCache,
    IOptions<PostsOptions> options,
    TimeProvider timeProvider)
    : ICommandHandler<ChangePostStatusCommand, PostDto>
{
    public async Task<Result<PostDto>> Handle(ChangePostStatusCommand request, CancellationToken cancellationToken)
    {
        var post = await posts.FirstOrDefaultAsync(new MyPostByIdSpec(request.Id), cancellationToken);
        if (post is null)
            return PostErrors.NotFound;

        var now = timeProvider.GetUtcNow();
        var result = request.Action switch
        {
            PostStatusAction.Publish => post.Publish(now),
            PostStatusAction.Unpublish => post.Unpublish(),
            PostStatusAction.Schedule => request.ScheduledAt is { } at ? post.Schedule(at, now) : PostErrors.ScheduleInPast,
            PostStatusAction.Archive => post.Archive(),
            PostStatusAction.Feature => Feature(post, true),
            PostStatusAction.Unfeature => Feature(post, false),
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.Action, "Unknown post status action.")
        };
        if (result.IsFailure)
            return result.Error;

        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            if (request.Action == PostStatusAction.Publish)
                await PostRevisionWriter.AddAsync(revisions, postRepository, options.Value, post, RevisionKind.Publish, now, ct);

            await unitOfWork.SaveChangesAsync(ct);
            return true;
        }, cancellationToken);

        await cache.RemoveByTagAsync(PostCache.Tag(post.Id), cancellationToken);

        // Muallif sahifasidagi post sonlari (kategoriya/teg daraxtlari) o'zgaradi.
        if (request.Action is PostStatusAction.Publish or PostStatusAction.Unpublish or PostStatusAction.Archive)
            await authorCache.InvalidateUserAsync(post.OwnerId, cancellationToken);

        return await PostDtoBuilder.BuildAsync(post, postRepository.GetVersion(post),
            new PostDtoSources(tags, media, revisions, storage), cancellationToken);
    }

    private static Result Feature(Post post, bool featured)
    {
        if (featured)
            post.Feature();
        else
            post.Unfeature();
        return Result.Success();
    }
}

public sealed record DeletePostCommand(Guid Id) : ICommand;

/// <summary>Soft delete (izohlar/reaksiyalar o'z modullarida PostDeletedDomainEvent orqali qayta ishlanadi).</summary>
internal sealed class DeletePostCommandHandler(
    IRepository<Post> posts,
    IUnitOfWork unitOfWork,
    ICacheService cache,
    IAuthorCacheInvalidator authorCache)
    : ICommandHandler<DeletePostCommand>
{
    public async Task<Result> Handle(DeletePostCommand request, CancellationToken cancellationToken)
    {
        var post = await posts.FirstOrDefaultAsync(new MyPostByIdSpec(request.Id), cancellationToken);
        if (post is null)
            return PostErrors.NotFound;

        posts.Remove(post);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await cache.RemoveByTagAsync(PostCache.Tag(post.Id), cancellationToken);
        await authorCache.InvalidateUserAsync(post.OwnerId, cancellationToken);
        return Result.Success();
    }
}
