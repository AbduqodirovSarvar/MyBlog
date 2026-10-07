using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using MyBlog.Application.Abstractions.Authorization;
using MyBlog.Application.Abstractions.Identity;
using MyBlog.Infrastructure.Identity;
using NSubstitute;

namespace MyBlog.Infrastructure.Tests.Identity;

public sealed class JwtTokenServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    private static readonly JwtOptions Options = new()
    {
        Issuer = "MyBlog",
        Audience = "MyBlog.Client",
        SigningKey = new string('k', 64),
        AccessTokenMinutes = 15,
        RefreshTokenDays = 14
    };

    private static readonly AuthUser User = new(
        Guid.CreateVersion7(), "ali@example.com", "ali", true, false,
        [Roles.Admin, Roles.User], [Permissions.Posts.Manage, Permissions.Users.View]);

    private static JwtTokenService CreateService(DateTimeOffset now)
    {
        var time = Substitute.For<TimeProvider>();
        time.GetUtcNow().Returns(now);
        return new JwtTokenService(Microsoft.Extensions.Options.Options.Create(Options), time);
    }

    [Fact]
    public async Task Token_contains_identity_role_and_permission_claims_and_validates()
    {
        // Haqiqiy vaqt: token validatsiyasi joriy soat bo'yicha lifetime'ni tekshiradi.
        var now = DateTimeOffset.UtcNow;
        var token = CreateService(now).CreateAccessToken(User);

        var result = await new JsonWebTokenHandler { MapInboundClaims = false }
            .ValidateTokenAsync(token.Token, JwtTokenService.CreateValidationParameters(Options));

        result.IsValid.ShouldBeTrue(result.Exception?.Message);
        var identity = result.ClaimsIdentity;
        identity.FindFirst("sub")!.Value.ShouldBe(User.Id.ToString());
        identity.FindFirst("email")!.Value.ShouldBe("ali@example.com");
        identity.FindFirst("unique_name")!.Value.ShouldBe("ali");
        identity.FindFirst("jti")!.Value.ShouldNotBeNullOrEmpty();
        identity.FindAll("role").Select(c => c.Value).ShouldBe([Roles.Admin, Roles.User], ignoreOrder: true);
        identity.FindAll(Permissions.ClaimType).Select(c => c.Value)
            .ShouldBe([Permissions.Posts.Manage, Permissions.Users.View], ignoreOrder: true);

        // NameClaimType/RoleClaimType JWT nomlariga moslangan.
        identity.Name.ShouldBe("ali");
        identity.RoleClaimType.ShouldBe("role");
    }

    [Fact]
    public void Expiration_matches_options_with_second_precision()
    {
        var token = CreateService(Now.AddMilliseconds(750)).CreateAccessToken(User);

        token.ExpiresAt.ShouldBe(Now.AddMinutes(15));
        new JsonWebToken(token.Token).ValidTo.ShouldBe(Now.AddMinutes(15).UtcDateTime);
    }

    [Fact]
    public async Task Token_signed_with_other_key_is_rejected()
    {
        var token = CreateService(DateTimeOffset.UtcNow).CreateAccessToken(User);
        var otherOptions = new JwtOptions
        {
            Issuer = Options.Issuer,
            Audience = Options.Audience,
            SigningKey = new string('x', 64)
        };

        var result = await new JsonWebTokenHandler()
            .ValidateTokenAsync(token.Token, JwtTokenService.CreateValidationParameters(otherOptions));

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Options_validation_requires_64_char_signing_key()
    {
        var options = new JwtOptions { Issuer = "i", Audience = "a", SigningKey = "short" };
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();

        System.ComponentModel.DataAnnotations.Validator
            .TryValidateObject(options, new(options), results, validateAllProperties: true)
            .ShouldBeFalse();
        results.ShouldContain(r => r.MemberNames.Contains(nameof(JwtOptions.SigningKey)));
    }
}

public sealed class RefreshTokenPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    private static RefreshToken Token(DateTimeOffset? revokedAt = null, string? replacedBy = null, int expiresInMinutes = 60) => new()
    {
        UserId = Guid.CreateVersion7(),
        TokenHash = "hash",
        CreatedAt = Now.AddDays(-1),
        ExpiresAt = Now.AddMinutes(expiresInMinutes),
        RevokedAt = revokedAt,
        ReplacedByTokenHash = replacedBy
    };

    [Fact]
    public void Missing_token_is_not_found() => RefreshTokenPolicy.Evaluate(null, Now).ShouldBe(RefreshTokenState.NotFound);

    [Fact]
    public void Active_token_is_active() => RefreshTokenPolicy.Evaluate(Token(), Now).ShouldBe(RefreshTokenState.Active);

    [Fact]
    public void Expired_token_is_expired() =>
        RefreshTokenPolicy.Evaluate(Token(expiresInMinutes: 0), Now).ShouldBe(RefreshTokenState.Expired);

    [Fact]
    public void Revoked_by_logout_is_revoked_not_reuse() =>
        RefreshTokenPolicy.Evaluate(Token(revokedAt: Now.AddMinutes(-1)), Now).ShouldBe(RefreshTokenState.Revoked);

    [Fact]
    public void Rotated_token_presented_again_is_reuse() =>
        RefreshTokenPolicy.Evaluate(Token(revokedAt: Now.AddMinutes(-1), replacedBy: "next"), Now)
            .ShouldBe(RefreshTokenState.Reused);

    [Fact]
    public void Rotated_and_expired_token_is_still_reuse() =>
        RefreshTokenPolicy.Evaluate(Token(revokedAt: Now.AddDays(-1), replacedBy: "next", expiresInMinutes: -5), Now)
            .ShouldBe(RefreshTokenState.Reused);
}

public sealed class TokenCryptoTests
{
    [Fact]
    public void Generated_refresh_tokens_are_random_url_safe_and_64_bytes()
    {
        var first = RefreshTokenCrypto.Generate();
        var second = RefreshTokenCrypto.Generate();

        first.ShouldNotBe(second);
        first.Length.ShouldBe(86); // 64 bayt Base64Url (padding'siz)
        first.ShouldAllBe(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_');
    }

    [Fact]
    public void Hash_is_deterministic_sha256_hex()
    {
        var hash = RefreshTokenCrypto.Hash("token");

        hash.ShouldBe(RefreshTokenCrypto.Hash("token"));
        hash.ShouldBe("3c469e9d6c5875d37a43f353d4f88e61fcf812c66eee3457465a40b0da4153e0");
        RefreshTokenCrypto.Hash("token2").ShouldNotBe(hash);
    }

    [Fact]
    public void Identity_tokens_round_trip_through_base64url()
    {
        const string raw = "CfDJ8+abc/def==";

        var encoded = IdentityTokenEncoding.Encode(raw);

        encoded.ShouldNotContain("+");
        encoded.ShouldNotContain("/");
        encoded.ShouldNotContain("=");
        IdentityTokenEncoding.Decode(encoded).ShouldBe(raw);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a")]      // noto'g'ri Base64Url uzunligi
    [InlineData("@@@@")]
    public void Invalid_identity_tokens_decode_to_null(string? encoded) =>
        IdentityTokenEncoding.Decode(encoded).ShouldBeNull();
}
