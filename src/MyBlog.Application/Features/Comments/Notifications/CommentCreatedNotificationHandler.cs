using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Application.Features.Comments.Abstractions;
using MyBlog.Domain.Comments.Events;

namespace MyBlog.Application.Features.Comments.Notifications;

/// <summary>
/// Yangi izoh haqida email: ildiz izoh — post muallifiga (NotifyOnComment); javob — ota izoh muallifiga (NotifyOnReply)
/// va post muallifiga (agar u boshqa odam bo'lsa, NotifyOnComment). O'ziga yuborilmaydi. Xat oluvchining
/// PreferredCulture tilida render qilinadi. Xatolik izoh yaratilishini buzmasligi uchun loglanadi va yutiladi.
/// </summary>
internal sealed partial class CommentCreatedNotificationHandler(
    ICommentRepository comments,
    IUserContactLookup contacts,
    IEmailTemplateRenderer renderer,
    IEmailQueue emailQueue,
    IOptions<CommentLinkOptions> linkOptions,
    ILogger<CommentCreatedNotificationHandler> logger) : IDomainEventHandler<CommentCreatedDomainEvent>
{
    public const string CommentOnPostTemplate = "comment-on-post";
    public const string CommentReplyTemplate = "comment-reply";

    private const int ExcerptMaxLength = 300;

    public async Task Handle(CommentCreatedDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        try
        {
            await NotifyAsync(domainEvent, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogNotificationFailed(logger, ex, domainEvent.CommentId);
        }
    }

    private async Task NotifyAsync(CommentCreatedDomainEvent e, CancellationToken cancellationToken)
    {
        var post = await comments.GetPostInfoAsync(e.PostId, cancellationToken);
        if (post is null)
            return;

        var recipients = new List<(Guid UserId, string Template)>(2);
        if (e.ParentAuthorId is { } parentAuthorId && parentAuthorId != e.AuthorId)
            recipients.Add((parentAuthorId, CommentReplyTemplate));
        if (post.OwnerId != e.AuthorId && post.OwnerId != e.ParentAuthorId)
            recipients.Add((post.OwnerId, CommentOnPostTemplate));

        if (recipients.Count == 0)
            return;

        var users = await comments.GetUsersAsync([e.AuthorId, .. recipients.Select(r => r.UserId)], cancellationToken);
        if (!users.TryGetValue(e.AuthorId, out var commenter))
            return;

        var comment = await comments.GetByIdAsync(e.CommentId, cancellationToken);
        var excerpt = Excerpt(comment?.Content);

        foreach (var (userId, template) in recipients)
        {
            if (!users.TryGetValue(userId, out var recipient))
                continue;

            var optedIn = template == CommentReplyTemplate ? recipient.NotifyOnReply : recipient.NotifyOnComment;
            if (!optedIn)
                continue;

            var email = await contacts.GetEmailAsync(userId, cancellationToken);
            if (string.IsNullOrWhiteSpace(email))
                continue;

            var culture = recipient.PreferredCulture;
            var values = new Dictionary<string, string>
            {
                ["recipientName"] = recipient.DisplayName,
                ["commenterName"] = commenter.DisplayName,
                ["postTitle"] = post.Title,
                ["commentExcerpt"] = excerpt,
                ["link"] = BuildLink(linkOptions.Value.BaseUrl, culture, post.OwnerUsername, post.Slug, e.CommentId)
            };

            var rendered = await renderer.RenderAsync(template, culture, values, cancellationToken);
            await emailQueue.EnqueueAsync(
                new EmailMessage(email, rendered.Subject, rendered.HtmlBody, rendered.TextBody), cancellationToken);
        }
    }

    /// <summary>{BaseUrl}/{culture}/{username}/{postSlug}#comment-{id}</summary>
    internal static string BuildLink(string baseUrl, string culture, string username, string postSlug, Guid commentId) =>
        $"{baseUrl.TrimEnd('/')}/{Uri.EscapeDataString(culture)}/{Uri.EscapeDataString(username)}/{Uri.EscapeDataString(postSlug)}#comment-{commentId}";

    private static string Excerpt(string? content)
    {
        if (string.IsNullOrEmpty(content))
            return string.Empty;

        return content.Length <= ExcerptMaxLength ? content : string.Concat(content.AsSpan(0, ExcerptMaxLength).TrimEnd(), "…");
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to send notifications for comment {CommentId}")]
    private static partial void LogNotificationFailed(ILogger logger, Exception exception, Guid commentId);
}
