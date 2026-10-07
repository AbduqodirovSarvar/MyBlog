using Microsoft.AspNetCore.Mvc;
using MyBlog.Api.Authorization;
using MyBlog.Api.Common;
using MyBlog.Application.Abstractions.Authorization;
using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Features.Categories.Common;
using MyBlog.Application.Features.Categories.CreateCategory;
using MyBlog.Application.Features.Categories.DeleteCategory;
using MyBlog.Application.Features.Categories.GetCategories;
using MyBlog.Application.Features.Categories.MoveCategory;
using MyBlog.Application.Features.Categories.UpdateCategory;
using MyBlog.Domain.Categories;

namespace MyBlog.Api.Controllers;

public sealed record UpdateCategoryRequest(
    IReadOnlyList<CategoryTranslationData> Translations,
    string? Slug,
    Guid? IconMediaId,
    bool IsActive = true);

public sealed record MoveCategoryRequest(Guid? NewParentId, int Order);

/// <summary>Joriy foydalanuvchining kategoriyalari.</summary>
[Route("api/my/categories")]
[HasPermission(Permissions.Categories.Manage)]
public sealed class MyCategoriesController(ISender sender) : ApiController(sender)
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<CategoryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Tree(CancellationToken cancellationToken) =>
        (await Sender.Send(new GetCategoryTreeQuery(), cancellationToken)).ToActionResult();

    [HttpGet("{id:guid}")]
    [ProducesResponseType<CategoryDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        (await Sender.Send(new GetCategoryQuery(id), cancellationToken)).ToActionResult();

    [HttpPost]
    [ProducesResponseType<CategoryDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateCategoryCommand command, CancellationToken cancellationToken) =>
        (await Sender.Send(command, cancellationToken)).ToCreatedResult(nameof(Get), c => new { id = c.Id });

    [HttpPut("{id:guid}")]
    [ProducesResponseType<CategoryDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, UpdateCategoryRequest request, CancellationToken cancellationToken) =>
        (await Sender.Send(new UpdateCategoryCommand(id, request.Translations, request.Slug, request.IconMediaId,
            request.IsActive), cancellationToken))
        .ToActionResult();

    [HttpPatch("{id:guid}/move")]
    public async Task<IActionResult> Move(Guid id, MoveCategoryRequest request, CancellationToken cancellationToken) =>
        (await Sender.Send(new MoveCategoryCommand(id, request.NewParentId, request.Order), cancellationToken))
        .ToActionResult();

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        (await Sender.Send(new DeleteCategoryCommand(id), cancellationToken)).ToActionResult();
}
