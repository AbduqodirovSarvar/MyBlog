using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Domain.Common;
using MyBlog.Domain.Users;

namespace MyBlog.Application.Features.Profile.Common;

/// <summary>Profil buyruqlari uchun umumiy servislar (konstruktorlarni qisqa saqlash uchun).</summary>
internal sealed record ProfileCommandContext(
    IRepository<UserProfile> Profiles,
    IUnitOfWork UnitOfWork,
    ICurrentUser CurrentUser,
    IAuthorCacheInvalidator Cache);

/// <summary>
/// Joriy foydalanuvchi profilini yuklaydi, o'zgarishni qo'llaydi, saqlaydi va ommaviy keshni tozalaydi.
/// </summary>
internal abstract class ProfileCommandHandler<TCommand>(ProfileCommandContext context) : ICommandHandler<TCommand>
    where TCommand : ICommand
{
    protected virtual ProfileSections Sections => ProfileSections.None;

    protected abstract Task<Result> ApplyAsync(UserProfile profile, TCommand command, CancellationToken cancellationToken);

    public async Task<Result> Handle(TCommand request, CancellationToken cancellationToken)
    {
        var profile = await context.Profiles.FirstOrDefaultAsync(
            new ProfileByIdSpec(context.CurrentUser.RequiredId, Sections), cancellationToken);
        if (profile is null)
            return UserProfileErrors.NotFound;

        var result = await ApplyAsync(profile, request, cancellationToken);
        if (result.IsFailure)
            return result;

        await context.UnitOfWork.SaveChangesAsync(cancellationToken);
        await context.Cache.InvalidateAsync(profile.Username, cancellationToken);
        return result;
    }
}

/// <summary>Natija qaytaradigan (masalan yangi element) profil buyruqlari uchun.</summary>
internal abstract class ProfileCommandHandler<TCommand, TResponse>(ProfileCommandContext context)
    : ICommandHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    protected virtual ProfileSections Sections => ProfileSections.None;

    protected abstract Task<Result<TResponse>> ApplyAsync(UserProfile profile, TCommand command, CancellationToken cancellationToken);

    public async Task<Result<TResponse>> Handle(TCommand request, CancellationToken cancellationToken)
    {
        var profile = await context.Profiles.FirstOrDefaultAsync(
            new ProfileByIdSpec(context.CurrentUser.RequiredId, Sections), cancellationToken);
        if (profile is null)
            return UserProfileErrors.NotFound;

        var result = await ApplyAsync(profile, request, cancellationToken);
        if (result.IsFailure)
            return result;

        await context.UnitOfWork.SaveChangesAsync(cancellationToken);
        await context.Cache.InvalidateAsync(profile.Username, cancellationToken);
        return result;
    }
}
