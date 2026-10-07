using FluentValidation;
using MyBlog.Application.Abstractions.Authorization;
using MyBlog.Application.Abstractions.Identity;
using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Common.Models;
using MyBlog.Domain.Common;

namespace MyBlog.Application.Features.Auth.Admin;

/// <param name="Search">Email yoki username bo'yicha qism-satr.</param>
public sealed record ListUsersQuery(int Page = 1, int PageSize = 20, string? Search = null, string? Role = null, bool? IsBlocked = null)
    : IQuery<PagedList<UserSummary>>;

public sealed record GetUserQuery(Guid UserId) : IQuery<UserSummary>;

internal sealed class ListUsersQueryValidator : AbstractValidator<ListUsersQuery>
{
    public ListUsersQueryValidator()
    {
        RuleFor(x => x.Search)
            .MaximumLength(256).WithErrorCode("Auth.SearchTooLong").WithMessage("Search text is too long.");
        RuleFor(x => x.Role)
            .Must(r => RoleNames.Normalize(r) is not null).When(x => !string.IsNullOrWhiteSpace(x.Role))
            .WithErrorCode("Auth.RoleNotFound").WithMessage("The role does not exist.");
    }
}

internal sealed class ListUsersQueryHandler(IIdentityService identityService)
    : IQueryHandler<ListUsersQuery, PagedList<UserSummary>>
{
    public async Task<Result<PagedList<UserSummary>>> Handle(ListUsersQuery request, CancellationToken cancellationToken)
    {
        var paging = new PageRequest(request.Page, request.PageSize);
        var filter = new UserListFilter(
            paging.SafePage,
            paging.SafePageSize,
            DomainRules.TrimToNull(request.Search),
            RoleNames.Normalize(request.Role),
            request.IsBlocked);

        return await identityService.ListUsersAsync(filter, cancellationToken);
    }
}

internal sealed class GetUserQueryHandler(IIdentityService identityService) : IQueryHandler<GetUserQuery, UserSummary>
{
    public async Task<Result<UserSummary>> Handle(GetUserQuery request, CancellationToken cancellationToken) =>
        await identityService.GetUserSummaryAsync(request.UserId, cancellationToken) is { } user
            ? user
            : AuthErrors.UserNotFound;
}

internal static class RoleNames
{
    /// <summary>Katta-kichik harfdan qat'i nazar kanonik rol nomi; noma'lum bo'lsa null.</summary>
    public static string? Normalize(string? role) =>
        string.IsNullOrWhiteSpace(role)
            ? null
            : Roles.All.FirstOrDefault(r => string.Equals(r, role.Trim(), StringComparison.OrdinalIgnoreCase));
}
