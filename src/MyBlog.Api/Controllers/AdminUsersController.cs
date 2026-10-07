using Microsoft.AspNetCore.Mvc;
using MyBlog.Api.Authorization;
using MyBlog.Api.Common;
using MyBlog.Application.Abstractions.Authorization;
using MyBlog.Application.Abstractions.Identity;
using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Common.Models;
using MyBlog.Application.Features.Auth.Admin;

namespace MyBlog.Api.Controllers;

/// <summary>Foydalanuvchilarni boshqarish (admin panel).</summary>
[Route("api/admin/users")]
[Produces("application/json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, ErrorProblemDetails.ContentType)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, ErrorProblemDetails.ContentType)]
public sealed class AdminUsersController(ISender sender) : ApiController(sender)
{
    /// <summary>Sahifalangan ro'yxat: email/username bo'yicha qidiruv, rol va bloklanganlik filtri.</summary>
    [HttpGet]
    [HasPermission(Permissions.Users.View)]
    [ProducesResponseType<PagedList<UserSummary>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, ErrorProblemDetails.ContentType)]
    public async Task<IActionResult> List([FromQuery] ListUsersQuery query, CancellationToken cancellationToken) =>
        (await Sender.Send(query, cancellationToken)).ToActionResult();

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Users.View)]
    [ProducesResponseType<UserSummary>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ErrorProblemDetails.ContentType)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        (await Sender.Send(new GetUserQuery(id), cancellationToken)).ToActionResult();

    /// <summary>Bloklash: foydalanuvchining barcha sessiyalari bekor qilinadi.</summary>
    [HttpPost("{id:guid}/block")]
    [HasPermission(Permissions.Users.Block)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, ErrorProblemDetails.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ErrorProblemDetails.ContentType)]
    public async Task<IActionResult> Block(Guid id, CancellationToken cancellationToken) =>
        (await Sender.Send(new BlockUserCommand(id), cancellationToken)).ToActionResult();

    [HttpPost("{id:guid}/unblock")]
    [HasPermission(Permissions.Users.Block)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ErrorProblemDetails.ContentType)]
    public async Task<IActionResult> Unblock(Guid id, CancellationToken cancellationToken) =>
        (await Sender.Send(new UnblockUserCommand(id), cancellationToken)).ToActionResult();

    /// <summary>Rol biriktirish (idempotent). Rollar: SuperAdmin, Admin, User.</summary>
    [HttpPut("{id:guid}/roles/{role}")]
    [HasPermission(Permissions.Users.ManageRoles)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, ErrorProblemDetails.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ErrorProblemDetails.ContentType)]
    public async Task<IActionResult> AssignRole(Guid id, string role, CancellationToken cancellationToken) =>
        (await Sender.Send(new AssignRoleCommand(id, role), cancellationToken)).ToActionResult();

    /// <summary>Rolni olib tashlash (idempotent). Oxirgi SuperAdmin'dan SuperAdmin rolini olib bo'lmaydi.</summary>
    [HttpDelete("{id:guid}/roles/{role}")]
    [HasPermission(Permissions.Users.ManageRoles)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, ErrorProblemDetails.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ErrorProblemDetails.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, ErrorProblemDetails.ContentType)]
    public async Task<IActionResult> RemoveRole(Guid id, string role, CancellationToken cancellationToken) =>
        (await Sender.Send(new RemoveRoleCommand(id, role), cancellationToken)).ToActionResult();
}
