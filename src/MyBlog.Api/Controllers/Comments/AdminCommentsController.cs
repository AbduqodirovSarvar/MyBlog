using Microsoft.AspNetCore.Mvc;
using MyBlog.Api.Authorization;
using MyBlog.Api.Common;
using MyBlog.Application.Abstractions.Authorization;
using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Common.Models;
using MyBlog.Application.Features.Comments.Common;
using MyBlog.Application.Features.Comments.DeleteComment;
using MyBlog.Application.Features.Comments.ListComments;

namespace MyBlog.Api.Controllers.Comments;

/// <summary>Izohlar moderatsiyasi (Comments.Moderate).</summary>
[Route("api/admin/comments")]
[HasPermission(Permissions.Comments.Moderate)]
[Produces("application/json")]
public sealed class AdminCommentsController(ISender sender) : ApiController(sender)
{
    [HttpGet]
    [ProducesResponseType<PagedList<CommentListItemDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        (await Sender.Send(new GetAdminCommentsQuery(search, page, pageSize), cancellationToken)).ToActionResult();

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        (await Sender.Send(new DeleteCommentCommand(id), cancellationToken)).ToActionResult();
}
