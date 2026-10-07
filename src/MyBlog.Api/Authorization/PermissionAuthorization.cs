using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using MyBlog.Application.Abstractions.Services;

namespace MyBlog.Api.Authorization;

/// <summary>
/// Endpoint'ni permission bilan himoyalaydi: <c>[HasPermission(Permissions.Posts.Manage)]</c>.
/// Policy'lar har bir permission uchun dinamik yaratiladi, ro'yxatdan o'tkazish shart emas.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class HasPermissionAttribute(string permission)
    : AuthorizeAttribute(PermissionPolicyProvider.PolicyPrefix + permission)
{
    public string Permission { get; } = permission;
}

internal sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;

internal sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
    : DefaultAuthorizationPolicyProvider(options)
{
    public const string PolicyPrefix = "Permission:";

    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(PolicyPrefix, StringComparison.Ordinal))
            return await base.GetPolicyAsync(policyName);

        return new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(policyName[PolicyPrefix.Length..]))
            .Build();
    }
}

/// <summary>Tekshiruv <see cref="ICurrentUser.HasPermission"/> orqali (SuperAdmin hamma ruxsatga ega).</summary>
internal sealed class PermissionAuthorizationHandler(ICurrentUser currentUser)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (currentUser.HasPermission(requirement.Permission))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}

internal static class PermissionAuthorizationExtensions
{
    public static IServiceCollection AddPermissionAuthorization(this IServiceCollection services)
    {
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        return services;
    }
}
