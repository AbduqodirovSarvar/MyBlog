using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Application.Features.Posts.Common;
using MyBlog.Application.Features.Profile.Common;
using MyBlog.Domain.Posts;

namespace MyBlog.Application.Features.Posts.Jobs;

/// <summary>Har daqiqada: vaqti kelgan Scheduled postlarni nashr qiladi (PostPublishedDomainEvent ham chiqadi).</summary>
internal sealed class ScheduledPostsPublisherJob(
    IRepository<Post> posts,
    IUnitOfWork unitOfWork,
    ICacheService cache,
    IAuthorCacheInvalidator authorCache,
    IOptions<PostsOptions> options,
    TimeProvider timeProvider,
    ILogger<ScheduledPostsPublisherJob> logger) : IRecurringJob
{
    public string Name => "posts-scheduled-publisher";

    public TimeSpan Interval => TimeSpan.FromMinutes(1);

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var due = await posts.ListAsync(new DueScheduledPostsSpec(now, options.Value.ScheduledPublishBatchSize), cancellationToken);

        var published = due.Where(p => p.IsDueForPublishing(now) && p.Publish(now).IsSuccess).ToList();
        if (published.Count == 0)
            return;

        await unitOfWork.SaveChangesAsync(cancellationToken);

        foreach (var post in published)
            await cache.RemoveByTagAsync(PostCache.Tag(post.Id), cancellationToken);

        foreach (var ownerId in published.Select(p => p.OwnerId).Distinct())
            await authorCache.InvalidateUserAsync(ownerId, cancellationToken);

        logger.LogInformation("Published {Count} scheduled posts", published.Count);
    }
}
