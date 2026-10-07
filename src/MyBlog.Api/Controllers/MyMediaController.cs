using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MyBlog.Api.Authorization;
using MyBlog.Api.Common;
using MyBlog.Api.RateLimiting;
using MyBlog.Application.Abstractions.Authorization;
using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Common.Models;
using MyBlog.Application.Features.Media;
using MyBlog.Application.Features.Media.Manage;
using MyBlog.Application.Features.Media.Upload;
using MyBlog.Domain.Media;

namespace MyBlog.Api.Controllers;

/// <summary>Joriy foydalanuvchining media kutubxonasi.</summary>
[Route("api/my/media")]
[HasPermission(Permissions.Media.Manage)]
public sealed class MyMediaController(ISender sender) : ApiController(sender)
{
    /// <summary>Rasm yuklash (multipart/form-data: file, altText?, caption?).</summary>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [EnableRateLimiting(RateLimitPolicies.Upload)]
    [MediaUploadLimits]
    [ProducesResponseType<MediaDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Upload([FromForm] UploadMediaForm form, CancellationToken cancellationToken)
    {
        if (form.File is not { Length: > 0 } file)
            return Problem(MediaErrors.FileRequired);

        await using var stream = file.OpenReadStream();
        var result = await Sender.Send(
            new UploadMediaCommand(stream, file.FileName, file.ContentType, file.Length, form.AltText, form.Caption),
            cancellationToken);

        return result.ToCreatedResult(nameof(Get), media => new { id = media.Id });
    }

    [HttpGet]
    [ProducesResponseType<PagedList<MediaDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 24, [FromQuery] string? type = null,
        CancellationToken cancellationToken = default) =>
        (await Sender.Send(new ListMyMediaQuery(page, pageSize, type), cancellationToken)).ToActionResult();

    [HttpGet("{id:guid}")]
    [ProducesResponseType<MediaDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        (await Sender.Send(new GetMyMediaQuery(id), cancellationToken)).ToActionResult();

    [HttpPut("{id:guid}")]
    [ProducesResponseType<MediaDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, UpdateMediaRequest request, CancellationToken cancellationToken) =>
        (await Sender.Send(new UpdateMediaCommand(id, request.AltText, request.Caption), cancellationToken)).ToActionResult();

    /// <summary>Faqat hech qayerda ishlatilmayotgan faylni o'chiradi (aks holda 409 Media.InUse).</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        (await Sender.Send(new DeleteMediaCommand(id), cancellationToken)).ToActionResult();
}

public sealed class UploadMediaForm
{
    public IFormFile? File { get; set; }
    public string? AltText { get; set; }
    public string? Caption { get; set; }
}

public sealed record UpdateMediaRequest(string? AltText, string? Caption);
