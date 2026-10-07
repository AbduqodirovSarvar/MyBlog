using MyBlog.Domain.Common;
using static MyBlog.Domain.Users.UserProfileConstraints;

namespace MyBlog.Domain.Users;

public sealed class Certificate : Entity
{
    private Certificate() { }

    private Certificate(Guid profileId) => ProfileId = profileId;

    public Guid ProfileId { get; private set; }
    public string Title { get; private set; } = null!;
    public string? Issuer { get; private set; }
    public DateOnly? IssuedAt { get; private set; }
    public DateOnly? ExpiresAt { get; private set; }
    public string? CredentialUrl { get; private set; }

    /// <summary>Sertifikat rasmi/fayli (MediaFile id).</summary>
    public Guid? MediaId { get; private set; }

    internal static Result<Certificate> Create(Guid profileId, string title, string? issuer, DateOnly? issuedAt,
        DateOnly? expiresAt, string? credentialUrl, Guid? mediaId)
    {
        var certificate = new Certificate(profileId);
        var result = certificate.Update(title, issuer, issuedAt, expiresAt, credentialUrl, mediaId);
        return result.IsFailure ? result.Error : certificate;
    }

    internal Result Update(string title, string? issuer, DateOnly? issuedAt, DateOnly? expiresAt,
        string? credentialUrl, Guid? mediaId)
    {
        var titleValue = DomainRules.TrimToNull(title);
        var issuerValue = DomainRules.TrimToNull(issuer);
        var urlValue = DomainRules.TrimToNull(credentialUrl);

        if (titleValue is null)
            return UserProfileErrors.CertificateTitleRequired;
        if (titleValue.Length > CertificateTitleMaxLength)
            return UserProfileErrors.CertificateTitleTooLong;
        if (issuerValue?.Length > IssuerMaxLength)
            return UserProfileErrors.IssuerTooLong;
        if (urlValue is not null && (urlValue.Length > UrlMaxLength || !DomainRules.IsValidHttpUrl(urlValue)))
            return UserProfileErrors.CredentialUrlInvalid;
        if (issuedAt is not null && expiresAt < issuedAt)
            return UserProfileErrors.CertificateExpiresBeforeIssued;

        Title = titleValue;
        Issuer = issuerValue;
        IssuedAt = issuedAt;
        ExpiresAt = expiresAt;
        CredentialUrl = urlValue;
        MediaId = mediaId == Guid.Empty ? null : mediaId;
        return Result.Success();
    }
}
