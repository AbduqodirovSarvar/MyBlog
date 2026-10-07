using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using MyBlog.Api.Authorization;
using MyBlog.Api.Common;
using MyBlog.Application.Abstractions.Authorization;
using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Common.Models;
using MyBlog.Application.Features.Posts;
using MyBlog.Application.Features.Posts.Common;
using MyBlog.Application.Features.Posts.Content;
using MyBlog.Application.Features.Posts.Manage;
using MyBlog.Domain.Posts;

namespace MyBlog.Api.Controllers;

/// <summary>Muallifning o'z postlarini boshqarishi (muharrir, holatlar, reviziyalar).</summary>
[Route("api/my/posts")]
[HasPermission(Permissions.Posts.Manage)]
public sealed class MyPostsController(ISender sender) : ApiController(sender)
{
    [HttpPost]
    [ProducesResponseType<PostDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(SavePostRequest request, CancellationToken cancellationToken)
    {
        var command = new CreatePostCommand(request.Title ?? string.Empty, request.Slug, request.Summary, request.CategoryId,
            request.Tags, request.CoverMediaId, request.Content.ToContentInput(), request.AllowComments ?? true, request.Seo?.ToInput());

        return (await Sender.Send(command, cancellationToken)).ToCreatedResult(nameof(Get), post => new { id = post.Id });
    }

    [HttpGet]
    [ProducesResponseType<PagedList<MyPostListItemDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] PostStatus? status = null,
        [FromQuery] Guid? categoryId = null,
        [FromQuery] Guid? tagId = null,
        [FromQuery] string? q = null,
        [FromQuery] MyPostSort sort = MyPostSort.Updated,
        [FromQuery] bool desc = true,
        CancellationToken cancellationToken = default) =>
        (await Sender.Send(new ListMyPostsQuery(page, pageSize, status, categoryId, tagId, q, sort, desc), cancellationToken))
        .ToActionResult();

    /// <summary>Muharrir uchun to'liq post (raw hujjat va version bilan).</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<PostDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        (await Sender.Send(new GetMyPostQuery(id), cancellationToken)).ToActionResult();

    /// <summary>Saqlash. <c>version</c> berilsa va post o'zgargan bo'lsa — 409.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType<PostDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, SavePostRequest request, CancellationToken cancellationToken)
    {
        var command = new UpdatePostCommand(id, request.Title ?? string.Empty, request.Slug, request.Summary, request.CategoryId,
            request.Tags, request.CoverMediaId, request.Content.ToContentInput(), request.AllowComments ?? true, request.Seo?.ToInput(),
            request.Version);

        return (await Sender.Send(command, cancellationToken)).ToActionResult();
    }

    /// <summary>Saqlanmagan holatni autosave reviziyasiga yozadi (post o'zgarmaydi).</summary>
    [HttpPut("{id:guid}/autosave")]
    [ProducesResponseType<AutosaveResultDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Autosave(Guid id, AutosavePostRequest request, CancellationToken cancellationToken) =>
        (await Sender.Send(new AutosavePostCommand(id, request.Title ?? string.Empty, request.Content.ToContentInput()), cancellationToken))
        .ToActionResult();

    [HttpPost("{id:guid}/publish")]
    [ProducesResponseType<PostDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Publish(Guid id, CancellationToken cancellationToken) =>
        ChangeStatusAsync(id, PostStatusAction.Publish, null, cancellationToken);

    [HttpPost("{id:guid}/unpublish")]
    [ProducesResponseType<PostDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Unpublish(Guid id, CancellationToken cancellationToken) =>
        ChangeStatusAsync(id, PostStatusAction.Unpublish, null, cancellationToken);

    [HttpPost("{id:guid}/schedule")]
    [ProducesResponseType<PostDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Schedule(Guid id, SchedulePostRequest request, CancellationToken cancellationToken) =>
        ChangeStatusAsync(id, PostStatusAction.Schedule, request.ScheduledAt, cancellationToken);

    [HttpPost("{id:guid}/archive")]
    [ProducesResponseType<PostDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Archive(Guid id, CancellationToken cancellationToken) =>
        ChangeStatusAsync(id, PostStatusAction.Archive, null, cancellationToken);

    [HttpPost("{id:guid}/feature")]
    [ProducesResponseType<PostDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Feature(Guid id, CancellationToken cancellationToken) =>
        ChangeStatusAsync(id, PostStatusAction.Feature, null, cancellationToken);

    [HttpPost("{id:guid}/unfeature")]
    [ProducesResponseType<PostDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Unfeature(Guid id, CancellationToken cancellationToken) =>
        ChangeStatusAsync(id, PostStatusAction.Unfeature, null, cancellationToken);

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        (await Sender.Send(new DeletePostCommand(id), cancellationToken)).ToActionResult();

    [HttpGet("{id:guid}/revisions")]
    [ProducesResponseType<IReadOnlyList<PostRevisionListItemDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Revisions(Guid id, CancellationToken cancellationToken) =>
        (await Sender.Send(new ListPostRevisionsQuery(id), cancellationToken)).ToActionResult();

    [HttpGet("{id:guid}/revisions/{revisionId:guid}")]
    [ProducesResponseType<PostRevisionDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Revision(Guid id, Guid revisionId, CancellationToken cancellationToken) =>
        (await Sender.Send(new GetPostRevisionQuery(id, revisionId), cancellationToken)).ToActionResult();

    /// <summary>Joriy holat reviziya sifatida saqlanadi, so'ng tanlangan reviziya qo'llanadi.</summary>
    [HttpPost("{id:guid}/revisions/{revisionId:guid}/restore")]
    [ProducesResponseType<PostDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Restore(Guid id, Guid revisionId, CancellationToken cancellationToken) =>
        (await Sender.Send(new RestorePostRevisionCommand(id, revisionId), cancellationToken)).ToActionResult();

    private async Task<IActionResult> ChangeStatusAsync(Guid id, PostStatusAction action, DateTimeOffset? scheduledAt,
        CancellationToken cancellationToken) =>
        (await Sender.Send(new ChangePostStatusCommand(id, action, scheduledAt), cancellationToken)).ToActionResult();
}

/// <param name="Raw">Muharrir hujjati: JSON obyekt/massiv (masalan TipTap doc) yoki JSON matni (string).</param>
public sealed record PostContentRequest(string? Format, string? Body, JsonElement? Raw)
{
    public ContentInput ToInput() => new(Format ?? string.Empty, Body ?? string.Empty, RawText(Raw));

    private static string? RawText(JsonElement? raw) => raw switch
    {
        null => null,
        { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => null,
        { ValueKind: JsonValueKind.String } element => element.GetString(),
        { } element => element.GetRawText()
    };
}

public sealed record PostSeoRequest(string? MetaTitle, string? MetaDescription, Guid? OgImageMediaId, string? CanonicalUrl)
{
    public PostSeoInput ToInput() => new(MetaTitle, MetaDescription, OgImageMediaId, CanonicalUrl);
}

/// <param name="Slug">Create: berilmasa sarlavhadan. Update: berilmasa o'zgarmaydi.</param>
/// <param name="Tags">Teg nomlari.</param>
/// <param name="Version">Faqat update: GET/oxirgi saqlashdan olingan version (optimistic concurrency).</param>
public sealed record SavePostRequest(
    string? Title,
    string? Slug,
    string? Summary,
    Guid? CategoryId,
    IReadOnlyList<string>? Tags,
    Guid? CoverMediaId,
    PostContentRequest? Content,
    bool? AllowComments,
    PostSeoRequest? Seo,
    uint? Version);

public sealed record AutosavePostRequest(string? Title, PostContentRequest? Content);

public sealed record SchedulePostRequest(DateTimeOffset ScheduledAt);

internal static class PostContentRequestExtensions
{
    public static ContentInput ToContentInput(this PostContentRequest? content) =>
        content?.ToInput() ?? new ContentInput(string.Empty, string.Empty);
}
