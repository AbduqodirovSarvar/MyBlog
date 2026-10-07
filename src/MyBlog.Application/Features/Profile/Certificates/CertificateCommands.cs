using FluentValidation;
using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Application.Features.Profile.Common;
using MyBlog.Domain.Common;
using MyBlog.Domain.Media;
using MyBlog.Domain.Users;
using static MyBlog.Domain.Users.UserProfileConstraints;

namespace MyBlog.Application.Features.Profile.Certificates;

public sealed record AddCertificateCommand(
    string Title,
    string? Issuer,
    DateOnly? IssuedAt,
    DateOnly? ExpiresAt,
    string? CredentialUrl,
    Guid? MediaId) : ICommand<CertificateDto>;

public sealed record UpdateCertificateCommand(
    Guid Id,
    string Title,
    string? Issuer,
    DateOnly? IssuedAt,
    DateOnly? ExpiresAt,
    string? CredentialUrl,
    Guid? MediaId) : ICommand;

public sealed record RemoveCertificateCommand(Guid Id) : ICommand;

internal sealed class AddCertificateCommandValidator : AbstractValidator<AddCertificateCommand>
{
    public AddCertificateCommandValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(CertificateTitleMaxLength);
        RuleFor(x => x.Issuer).MaximumLength(IssuerMaxLength);
        RuleFor(x => x.CredentialUrl).MaximumLength(UrlMaxLength);
    }
}

internal sealed class UpdateCertificateCommandValidator : AbstractValidator<UpdateCertificateCommand>
{
    public UpdateCertificateCommandValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(CertificateTitleMaxLength);
        RuleFor(x => x.Issuer).MaximumLength(IssuerMaxLength);
        RuleFor(x => x.CredentialUrl).MaximumLength(UrlMaxLength);
    }
}

internal static class CertificateMedia
{
    /// <summary>Sertifikat fayli bo'lsa, u joriy foydalanuvchiga tegishli bo'lishi kerak (ownership filtri).</summary>
    public static async Task<Result> EnsureExistsAsync(IReadRepository<MediaFile> mediaFiles, Guid? mediaId,
        CancellationToken cancellationToken)
    {
        if (mediaId is not { } id || id == Guid.Empty)
            return Result.Success();

        return await mediaFiles.GetByIdAsync(id, cancellationToken) is null
            ? MediaErrors.NotFound
            : Result.Success();
    }
}

internal sealed class AddCertificateCommandHandler(
    ProfileCommandContext context,
    IReadRepository<MediaFile> mediaFiles,
    IMediaUrlResolver mediaUrls) : ProfileCommandHandler<AddCertificateCommand, CertificateDto>(context)
{
    protected override ProfileSections Sections => ProfileSections.Certificates;

    protected override async Task<Result<CertificateDto>> ApplyAsync(UserProfile profile, AddCertificateCommand c,
        CancellationToken cancellationToken)
    {
        var media = await CertificateMedia.EnsureExistsAsync(mediaFiles, c.MediaId, cancellationToken);
        if (media.IsFailure)
            return media.Error;

        var certificate = profile.AddCertificate(c.Title, c.Issuer, c.IssuedAt, c.ExpiresAt, c.CredentialUrl, c.MediaId);
        if (certificate.IsFailure)
            return certificate.Error;

        var urls = await mediaUrls.ResolveAsync([certificate.Value.MediaId], publicAccess: false,
            cancellationToken: cancellationToken);
        return ProfileMapper.ToDto(certificate.Value, urls);
    }
}

internal sealed class UpdateCertificateCommandHandler(ProfileCommandContext context, IReadRepository<MediaFile> mediaFiles)
    : ProfileCommandHandler<UpdateCertificateCommand>(context)
{
    protected override ProfileSections Sections => ProfileSections.Certificates;

    protected override async Task<Result> ApplyAsync(UserProfile profile, UpdateCertificateCommand c,
        CancellationToken cancellationToken)
    {
        // O'zgarmagan fayl qayta tekshirilmaydi
        var current = profile.Certificates.FirstOrDefault(x => x.Id == c.Id);
        if (current is not null && current.MediaId != c.MediaId)
        {
            var media = await CertificateMedia.EnsureExistsAsync(mediaFiles, c.MediaId, cancellationToken);
            if (media.IsFailure)
                return media;
        }

        return profile.UpdateCertificate(c.Id, c.Title, c.Issuer, c.IssuedAt, c.ExpiresAt, c.CredentialUrl, c.MediaId);
    }
}

internal sealed class RemoveCertificateCommandHandler(ProfileCommandContext context)
    : ProfileCommandHandler<RemoveCertificateCommand>(context)
{
    protected override ProfileSections Sections => ProfileSections.Certificates;

    protected override Task<Result> ApplyAsync(UserProfile profile, RemoveCertificateCommand command,
        CancellationToken cancellationToken) =>
        Task.FromResult(profile.RemoveCertificate(command.Id));
}
