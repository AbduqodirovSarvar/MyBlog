using Microsoft.AspNetCore.Mvc;
using MyBlog.Api.Authorization;
using MyBlog.Api.Common;
using MyBlog.Application.Abstractions.Authorization;
using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Features.Tags.Common;
using MyBlog.Application.Features.Tags.ManageTags;

namespace MyBlog.Api.Controllers;

public sealed record TagNameRequest(string Name);

/// <summary>Joriy foydalanuvchining teglari.</summary>
[Route("api/my/tags")]
[HasPermission(Permissions.Tags.Manage)]
public sealed class MyTagsController(ISender sender) : ApiController(sender)
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<TagDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] string? search, CancellationToken cancellationToken) =>
        (await Sender.Send(new GetTagsQuery(search), cancellationToken)).ToActionResult();

    [HttpPost]
    [ProducesResponseType<TagDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(TagNameRequest request, CancellationToken cancellationToken) =>
        (await Sender.Send(new CreateTagCommand(request.Name), cancellationToken))
        .ToCreatedResult(t => $"/api/my/tags?search={Uri.EscapeDataString(t.Slug)}");

    [HttpPut("{id:guid}")]
    [ProducesResponseType<TagDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Rename(Guid id, TagNameRequest request, CancellationToken cancellationToken) =>
        (await Sender.Send(new RenameTagCommand(id, request.Name), cancellationToken)).ToActionResult();

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        (await Sender.Send(new DeleteTagCommand(id), cancellationToken)).ToActionResult();
}
