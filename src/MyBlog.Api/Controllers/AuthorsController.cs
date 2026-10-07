using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyBlog.Api.Common;
using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Common.Models;
using MyBlog.Application.Features.Authors.Common;
using MyBlog.Application.Features.Authors.GetAuthorProfile;
using MyBlog.Application.Features.Authors.GetAuthors;
using MyBlog.Application.Features.Authors.GetAuthorTaxonomy;
using MyBlog.Application.Features.Categories.Common;
using MyBlog.Application.Features.Tags.Common;

namespace MyBlog.Api.Controllers;

/// <summary>Mualliflar haqida ommaviy ma'lumot (anonim). Tizim yopiq bo'lsa (PublicReadOfPublishedContent=false) — 404.</summary>
[Route("api/public/authors")]
[AllowAnonymous]
[PublicContent]
public sealed class AuthorsController(ISender sender) : ApiController(sender)
{
    [HttpGet]
    [ProducesResponseType<PagedList<AuthorSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] string? search, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default) =>
        (await Sender.Send(new GetAuthorsQuery(search, page, pageSize), cancellationToken)).ToActionResult();

    [HttpGet("{username}")]
    [ProducesResponseType<AuthorProfileDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(string username, CancellationToken cancellationToken) =>
        (await Sender.Send(new GetAuthorProfileQuery(username), cancellationToken)).ToActionResult();

    [HttpGet("{username}/categories")]
    [ProducesResponseType<IReadOnlyList<PublicCategoryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Categories(string username, CancellationToken cancellationToken) =>
        (await Sender.Send(new GetAuthorCategoriesQuery(username), cancellationToken)).ToActionResult();

    [HttpGet("{username}/tags")]
    [ProducesResponseType<IReadOnlyList<PublicTagDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Tags(string username, CancellationToken cancellationToken) =>
        (await Sender.Send(new GetAuthorTagsQuery(username), cancellationToken)).ToActionResult();
}
