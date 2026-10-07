using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MyBlog.Api.Authorization;
using MyBlog.Api.Common;
using MyBlog.Api.RateLimiting;
using MyBlog.Application.Abstractions.Authorization;
using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Common.Models;
using MyBlog.Application.Features.Comments.Abstractions;
using MyBlog.Application.Features.Comments.Common;
using MyBlog.Application.Features.Comments.CreateComment;
using MyBlog.Application.Features.Comments.DeleteComment;
using MyBlog.Application.Features.Comments.EditComment;
using MyBlog.Application.Features.Comments.GetComment;
using MyBlog.Application.Features.Comments.GetCommentCount;
using MyBlog.Application.Features.Comments.GetPostComments;
using MyBlog.Application.Features.Comments.ListComments;

namespace MyBlog.Api.Controllers.Comments;

/// <param name="Content">Plain text (HTML sifatida talqin qilinmaydi).</param>
/// <param name="ParentId">Javob bo'lsa ota izoh id'si.</param>
public sealed record CreateCommentRequest(string? Content, Guid? ParentId);

public sealed record UpdateCommentRequest(string? Content);

/// <summary>Post izohlari: o'qish anonim, yozish Comments.Write ruxsati bilan.</summary>
[Route("api")]
[Produces("application/json")]
public sealed class CommentsController(ISender sender) : ApiController(sender)
{
    /// <summary>Ildiz izohlar sahifasi (har biri javoblar daraxti bilan). sort: oldest | newest | top.</summary>
    [HttpGet("posts/{postId:guid}/comments")]
    [AllowAnonymous]
    [ProducesResponseType<PagedList<CommentDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPostComments(Guid postId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] CommentSort sort = CommentSort.Oldest, CancellationToken cancellationToken = default) =>
        (await Sender.Send(new GetPostCommentsQuery(postId, page, pageSize, sort), cancellationToken)).ToActionResult();

    [HttpGet("posts/{postId:guid}/comments/count")]
    [AllowAnonymous]
    [ProducesResponseType<CommentCountDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCommentCount(Guid postId, CancellationToken cancellationToken) =>
        (await Sender.Send(new GetCommentCountQuery(postId), cancellationToken)).ToActionResult();

    [HttpGet("comments/{id:guid}")]
    [AllowAnonymous]
    [ProducesResponseType<CommentDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetComment(Guid id, CancellationToken cancellationToken) =>
        (await Sender.Send(new GetCommentQuery(id), cancellationToken)).ToActionResult();

    [HttpPost("posts/{postId:guid}/comments")]
    [HasPermission(Permissions.Comments.Write)]
    [EnableRateLimiting(RateLimitPolicies.Comments)]
    [ProducesResponseType<CommentDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(Guid postId, CreateCommentRequest request, CancellationToken cancellationToken) =>
        (await Sender.Send(new CreateCommentCommand(postId, request.Content ?? string.Empty, request.ParentId), cancellationToken))
        .ToCreatedResult(comment => $"/api/comments/{comment.Id}");

    [HttpPut("comments/{id:guid}")]
    [HasPermission(Permissions.Comments.Write)]
    [EnableRateLimiting(RateLimitPolicies.Comments)]
    [ProducesResponseType<CommentDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, UpdateCommentRequest request, CancellationToken cancellationToken) =>
        (await Sender.Send(new EditCommentCommand(id, request.Content ?? string.Empty), cancellationToken)).ToActionResult();

    /// <summary>Muallif, post egasi yoki moderator o'chira oladi (tekshiruv handler'da).</summary>
    [HttpDelete("comments/{id:guid}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        (await Sender.Send(new DeleteCommentCommand(id), cancellationToken)).ToActionResult();

    /// <summary>Joriy foydalanuvchi yozgan izohlar.</summary>
    [HttpGet("my/comments")]
    [Authorize]
    [ProducesResponseType<PagedList<CommentListItemDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyComments([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        (await Sender.Send(new GetMyCommentsQuery(page, pageSize), cancellationToken)).ToActionResult();
}
