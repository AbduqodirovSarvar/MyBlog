using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using MyBlog.Api.Authorization;
using MyBlog.Api.Common;
using MyBlog.Application.Abstractions.Authorization;
using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Features.Reactions.Common;
using MyBlog.Application.Features.Reactions.RemoveReaction;
using MyBlog.Application.Features.Reactions.SetReaction;
using MyBlog.Domain.Reactions;

namespace MyBlog.Api.Controllers.Reactions;

/// <param name="Type">"Like" yoki "Dislike".</param>
public sealed record SetReactionRequest(
    [property: JsonConverter(typeof(JsonStringEnumConverter<ReactionType>))] ReactionType Type);

/// <summary>Like/Dislike: PUT — qo'yish yoki almashtirish, DELETE — olib tashlash. Javob: hisoblagichlar va mening reaksiyam.</summary>
[Route("api")]
[HasPermission(Permissions.Reactions.Write)]
[Produces("application/json")]
public sealed class ReactionsController(ISender sender) : ApiController(sender)
{
    [HttpPut("posts/{id:guid}/reaction")]
    [ProducesResponseType<ReactionSummaryDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> SetPostReaction(Guid id, SetReactionRequest request, CancellationToken cancellationToken) =>
        SetAsync(ReactionTargetType.Post, id, request, cancellationToken);

    [HttpDelete("posts/{id:guid}/reaction")]
    [ProducesResponseType<ReactionSummaryDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> RemovePostReaction(Guid id, CancellationToken cancellationToken) =>
        RemoveAsync(ReactionTargetType.Post, id, cancellationToken);

    [HttpPut("comments/{id:guid}/reaction")]
    [ProducesResponseType<ReactionSummaryDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> SetCommentReaction(Guid id, SetReactionRequest request, CancellationToken cancellationToken) =>
        SetAsync(ReactionTargetType.Comment, id, request, cancellationToken);

    [HttpDelete("comments/{id:guid}/reaction")]
    [ProducesResponseType<ReactionSummaryDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> RemoveCommentReaction(Guid id, CancellationToken cancellationToken) =>
        RemoveAsync(ReactionTargetType.Comment, id, cancellationToken);

    private async Task<IActionResult> SetAsync(ReactionTargetType targetType, Guid id, SetReactionRequest request,
        CancellationToken cancellationToken) =>
        (await Sender.Send(new SetReactionCommand(targetType, id, request.Type), cancellationToken)).ToActionResult();

    private async Task<IActionResult> RemoveAsync(ReactionTargetType targetType, Guid id, CancellationToken cancellationToken) =>
        (await Sender.Send(new RemoveReactionCommand(targetType, id), cancellationToken)).ToActionResult();
}
