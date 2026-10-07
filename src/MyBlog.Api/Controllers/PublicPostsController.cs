using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyBlog.Api.Common;
using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Common.Models;
using MyBlog.Application.Features.Posts;
using MyBlog.Application.Features.Posts.Abstractions;
using MyBlog.Application.Features.Posts.Public;

namespace MyBlog.Api.Controllers;

/// <summary>Nashr qilingan postlar (anonim). Tizim yopiq bo'lsa (PublicReadOfPublishedContent=false) — 404.</summary>
[AllowAnonymous]
[PublicContent]
public sealed class PublicPostsController(ISender sender) : ApiController(sender)
{
    /// <param name="author">Muallif username'i.</param>
    /// <param name="category">Kategoriya slug'i (author bilan birga — shu muallifniki).</param>
    /// <param name="tag">Teg slug'i.</param>
    /// <param name="q">To'liq matnli qidiruv.</param>
    /// <param name="sort">newest | popular | mostLiked | relevance.</param>
    [HttpGet("api/public/posts")]
    [ProducesResponseType<PagedList<PublicPostSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? author = null,
        [FromQuery] string? category = null,
        [FromQuery] bool includeSubcategories = false,
        [FromQuery] string? tag = null,
        [FromQuery] string? q = null,
        [FromQuery] bool? featured = null,
        [FromQuery] PublicPostSort? sort = null,
        CancellationToken cancellationToken = default) =>
        (await Sender.Send(new ListPublicPostsQuery(page, pageSize, author, category, includeSubcategories, tag, q, featured, sort),
            cancellationToken)).ToActionResult();

    /// <summary>Post sahifasi (slug muallif bo'yicha unikal, shuning uchun yo'lda username bor).</summary>
    [HttpGet("api/public/authors/{username}/posts/{slug}")]
    [ProducesResponseType<PublicPostDetailDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(string username, string slug, CancellationToken cancellationToken) =>
        (await Sender.Send(new GetPublicPostQuery(username, slug), cancellationToken)).ToActionResult();
}
