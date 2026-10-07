using MyBlog.Application.Abstractions.Identity;
using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Application.Features.Auth.Common;
using MyBlog.Domain.Common;

namespace MyBlog.Application.Features.Auth.GetCurrentUser;

public sealed record GetCurrentUserQuery : IQuery<CurrentUserResponse>;

/// <summary>Ma'lumotlar token'dan emas, bazadan olinadi (rollar o'zgargan bo'lishi mumkin).</summary>
internal sealed class GetCurrentUserQueryHandler(
    ICurrentUser currentUser,
    IIdentityService identityService,
    UserProfileReader profileReader) : IQueryHandler<GetCurrentUserQuery, CurrentUserResponse>
{
    public async Task<Result<CurrentUserResponse>> Handle(GetCurrentUserQuery request, CancellationToken cancellationToken)
    {
        var user = await identityService.FindByIdAsync(currentUser.RequiredId, cancellationToken);
        if (user is null || user.IsBlocked)
            return AuthErrors.SessionInvalid;

        return await profileReader.GetCurrentUserAsync(user, cancellationToken);
    }
}
