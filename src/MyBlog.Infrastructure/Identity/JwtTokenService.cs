using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MyBlog.Application.Abstractions.Authorization;
using MyBlog.Application.Abstractions.Identity;

namespace MyBlog.Infrastructure.Identity;

/// <summary>JWT claim nomlari. MapInboundClaims=false — Api'dagi HttpCurrentUser shu nomlarni o'qiydi.</summary>
internal static class JwtClaimNames
{
    public const string Subject = JwtRegisteredClaimNames.Sub;
    public const string Email = JwtRegisteredClaimNames.Email;
    public const string UniqueName = JwtRegisteredClaimNames.UniqueName;
    public const string TokenId = JwtRegisteredClaimNames.Jti;
    public const string Role = "role";
}

internal sealed class JwtTokenService(IOptions<JwtOptions> options, TimeProvider timeProvider) : ITokenService
{
    private static readonly JsonWebTokenHandler Handler = new();

    public AccessToken CreateAccessToken(AuthUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var jwt = options.Value;

        // JWT vaqtlari soniya aniqligida — javobdagi ExpiresAt token bilan bir xil bo'lsin.
        var now = DateTimeOffset.FromUnixTimeSeconds(timeProvider.GetUtcNow().ToUnixTimeSeconds());
        var expiresAt = now.AddMinutes(jwt.AccessTokenMinutes);

        List<Claim> claims =
        [
            new(JwtClaimNames.Subject, user.Id.ToString()),
            new(JwtClaimNames.Email, user.Email),
            new(JwtClaimNames.UniqueName, user.UserName),
            new(JwtClaimNames.TokenId, Guid.NewGuid().ToString("N"))
        ];
        claims.AddRange(user.Roles.Select(role => new Claim(JwtClaimNames.Role, role)));
        claims.AddRange(user.Permissions.Distinct(StringComparer.Ordinal).Select(p => new Claim(Permissions.ClaimType, p)));

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(CreateSigningKey(jwt), SecurityAlgorithms.HmacSha256)
        };

        return new AccessToken(Handler.CreateToken(descriptor), expiresAt);
    }

    public static SymmetricSecurityKey CreateSigningKey(JwtOptions options) =>
        new(Encoding.UTF8.GetBytes(options.SigningKey));

    /// <summary>JwtBearer va testlar uchun umumiy tekshiruv parametrlari.</summary>
    public static TokenValidationParameters CreateValidationParameters(JwtOptions options) => new()
    {
        ValidateIssuer = true,
        ValidIssuer = options.Issuer,
        ValidateAudience = true,
        ValidAudience = options.Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = CreateSigningKey(options),
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        ValidateLifetime = true,
        RequireExpirationTime = true,
        ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = JwtClaimNames.UniqueName,
        RoleClaimType = JwtClaimNames.Role
    };
}
