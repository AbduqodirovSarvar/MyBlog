using System.Security.Claims;
using MyBlog.Application.Abstractions.Authorization;
using MyBlog.Application.Abstractions.Services;

namespace MyBlog.Api.Services;

/// <summary>
/// Joriy foydalanuvchi HttpContext.User claim'laridan. HttpContext bo'lmasa (background job) — anonim.
/// SuperAdmin barcha ruxsatlarga ega.
/// </summary>
internal sealed class HttpCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    public Guid? Id =>
        Guid.TryParse(FindFirst("sub", ClaimTypes.NameIdentifier), out var id) ? id : null;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true && Id is not null;

    public string? UserName => FindFirst("unique_name", ClaimTypes.Name, "name");

    public string? Email => FindFirst("email", ClaimTypes.Email);

    public IReadOnlyCollection<string> Roles =>
        Principal?.Claims
            .Where(c => c.Type is ClaimTypes.Role or "role")
            .Select(c => c.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? [];

    public bool IsInRole(string role) => Roles.Contains(role, StringComparer.Ordinal);

    public bool HasPermission(string permission) =>
        IsAuthenticated
        && (IsInRole(Application.Abstractions.Authorization.Roles.SuperAdmin)
            || Principal!.HasClaim(Permissions.ClaimType, permission));

    private string? FindFirst(params string[] claimTypes)
    {
        var principal = Principal;
        if (principal is null)
            return null;

        foreach (var type in claimTypes)
        {
            if (principal.FindFirst(type)?.Value is { Length: > 0 } value)
                return value;
        }

        return null;
    }
}
