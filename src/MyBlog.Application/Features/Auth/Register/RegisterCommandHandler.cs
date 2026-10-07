using Microsoft.Extensions.Options;
using MyBlog.Application.Abstractions.Identity;
using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Application.Features.Auth.Common;
using MyBlog.Domain.Common;
using MyBlog.Domain.Users;

namespace MyBlog.Application.Features.Auth.Register;

/// <summary>
/// Identity foydalanuvchisi va UserProfile bitta tranzaksiyada yaratiladi; tasdiqlash xati commit'dan keyin yuboriladi.
/// </summary>
internal sealed class RegisterCommandHandler(
    IIdentityService identityService,
    IRepository<UserProfile> profiles,
    IUnitOfWork unitOfWork,
    ILocalizer localizer,
    AuthEmailService emailService,
    IOptions<AuthOptions> authOptions) : ICommandHandler<RegisterCommand, RegisterResponse>
{
    public async Task<Result<RegisterResponse>> Handle(RegisterCommand request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim();
        var userName = request.UserName.Trim().ToLowerInvariant();

        if (await identityService.IsEmailTakenAsync(email, cancellationToken))
            return AuthErrors.EmailTaken;

        if (await identityService.IsUserNameTakenAsync(userName, cancellationToken)
            || await profiles.AnyAsync(new ProfileUsernameExistsSpec(userName), cancellationToken))
            return AuthErrors.UsernameTaken;

        var culture = Cultures.Normalize(request.Culture) ?? localizer.CurrentCulture;

        var created = await unitOfWork.ExecuteInTransactionAsync<Result<Guid>>(async ct =>
        {
            var userId = await identityService.CreateUserAsync(email, userName, request.Password, ct);
            if (userId.IsFailure)
                return userId.Error;

            var profile = UserProfile.Create(userId.Value, userName, request.FirstName, request.LastName, culture);
            if (profile.IsFailure)
                return profile.Error;

            profiles.Add(profile.Value);
            await unitOfWork.SaveChangesAsync(ct);
            return userId.Value;
        }, cancellationToken);

        if (created.IsFailure)
            return created.Error;

        var requiresConfirmation = authOptions.Value.RequireConfirmedEmail;
        var user = await identityService.FindByIdAsync(created.Value, cancellationToken);
        if (user is not null && !user.EmailConfirmed)
            await emailService.SendEmailConfirmationAsync(user, cancellationToken);

        return new RegisterResponse(created.Value, requiresConfirmation);
    }
}
