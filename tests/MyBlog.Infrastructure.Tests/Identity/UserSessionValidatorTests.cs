using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using MyBlog.Infrastructure.Caching;
using MyBlog.Infrastructure.Identity;
using NSubstitute;

namespace MyBlog.Infrastructure.Tests.Identity;

public sealed class UserSessionValidatorTests : IAsyncDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly Guid _userId = Guid.CreateVersion7();
    private readonly IUserSessionStateReader _reader = Substitute.For<IUserSessionStateReader>();
    private readonly ServiceProvider _cacheProvider = new ServiceCollection().AddHybridCache().Services.BuildServiceProvider();
    private readonly UserSessionValidator _validator;

    public UserSessionValidatorTests() =>
        _validator = new UserSessionValidator(_reader, new HybridCacheService(_cacheProvider.GetRequiredService<HybridCache>()));

    private void ArrangeState(string stamp, bool isBlocked = false, bool exists = true) =>
        _reader.ReadAsync(_userId, Arg.Any<CancellationToken>())
            .Returns(exists ? new UserSessionState(true, isBlocked, SessionVersions.From(stamp)) : UserSessionState.Missing);

    [Fact]
    public async Task Matching_version_is_valid_and_state_is_cached()
    {
        ArrangeState("stamp-1");

        (await _validator.IsValidAsync(_userId, SessionVersions.From("stamp-1"), Ct)).ShouldBeTrue();
        (await _validator.IsValidAsync(_userId, SessionVersions.From("stamp-1"), Ct)).ShouldBeTrue();

        await _reader.Received(1).ReadAsync(_userId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Missing_version_is_rejected_without_database_access()
    {
        (await _validator.IsValidAsync(_userId, null, Ct)).ShouldBeFalse();
        (await _validator.IsValidAsync(_userId, "", Ct)).ShouldBeFalse();

        await _reader.DidNotReceiveWithAnyArgs().ReadAsync(default, Ct);
    }

    [Fact]
    public async Task Blocked_or_deleted_user_is_rejected_even_with_matching_version()
    {
        ArrangeState("stamp-1", isBlocked: true);
        (await _validator.IsValidAsync(_userId, SessionVersions.From("stamp-1"), Ct)).ShouldBeFalse();

        await _validator.InvalidateAsync(_userId, Ct);
        ArrangeState("stamp-1", exists: false);
        (await _validator.IsValidAsync(_userId, SessionVersions.From("stamp-1"), Ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task Invalidation_makes_stamp_change_visible_immediately()
    {
        ArrangeState("stamp-1");
        (await _validator.IsValidAsync(_userId, SessionVersions.From("stamp-1"), Ct)).ShouldBeTrue();

        // Bloklash/parol/logout-all: stamp yangilanadi va kesh tozalanadi.
        ArrangeState("stamp-2");
        await _validator.InvalidateAsync(_userId, Ct);

        (await _validator.IsValidAsync(_userId, SessionVersions.From("stamp-1"), Ct)).ShouldBeFalse();
        (await _validator.IsValidAsync(_userId, SessionVersions.From("stamp-2"), Ct)).ShouldBeTrue();
    }

    [Fact]
    public async Task Stale_cache_does_not_reject_new_token_and_is_refreshed()
    {
        ArrangeState("stamp-1");
        (await _validator.IsValidAsync(_userId, SessionVersions.From("stamp-1"), Ct)).ShouldBeTrue();

        // Kesh tozalanmagan (masalan boshqa instansiyada o'zgargan) — yangi token baribir qabul qilinadi.
        ArrangeState("stamp-2");
        (await _validator.IsValidAsync(_userId, SessionVersions.From("stamp-2"), Ct)).ShouldBeTrue();

        // Kesh yangilandi: eski token endi keshdan ham rad etiladi.
        (await _validator.IsValidAsync(_userId, SessionVersions.From("stamp-1"), Ct)).ShouldBeFalse();
    }

    [Fact]
    public void Session_version_is_a_stable_hash_not_the_raw_stamp()
    {
        var version = SessionVersions.From("ABCDEF123456");

        version.ShouldBe(SessionVersions.From("ABCDEF123456"));
        version.ShouldNotBe(SessionVersions.From("ABCDEF123457"));
        version.ShouldNotContain("ABCDEF");
        SessionVersions.From(null).ShouldBe(SessionVersions.From(string.Empty));
        SessionVersions.AreEqual(version, null).ShouldBeFalse();
    }

    public async ValueTask DisposeAsync() => await _cacheProvider.DisposeAsync();
}

public sealed class UserSessionTokenValidationTests
{
    private static TokenValidatedContext CreateContext(IUserSessionValidator validator, params Claim[] claims)
    {
        var services = new ServiceCollection().AddSingleton(validator).BuildServiceProvider();
        var httpContext = new DefaultHttpContext { RequestServices = services };
        var scheme = new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler));

        return new TokenValidatedContext(httpContext, scheme, new JwtBearerOptions())
        {
            Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"))
        };
    }

    [Fact]
    public async Task Valid_session_keeps_principal()
    {
        var userId = Guid.CreateVersion7();
        var validator = Substitute.For<IUserSessionValidator>();
        validator.IsValidAsync(userId, "v1", Arg.Any<CancellationToken>()).Returns(true);
        var context = CreateContext(validator, new Claim("sub", userId.ToString()), new Claim("sv", "v1"));

        await UserSessionTokenValidation.OnTokenValidatedAsync(context);

        context.Result.ShouldBeNull();
    }

    [Fact]
    public async Task Invalid_session_fails_authentication()
    {
        var userId = Guid.CreateVersion7();
        var validator = Substitute.For<IUserSessionValidator>();
        validator.IsValidAsync(userId, "old", Arg.Any<CancellationToken>()).Returns(false);
        var context = CreateContext(validator, new Claim("sub", userId.ToString()), new Claim("sv", "old"));

        await UserSessionTokenValidation.OnTokenValidatedAsync(context);

        context.Result.ShouldNotBeNull();
        context.Result.Failure.ShouldNotBeNull();
    }

    [Fact]
    public async Task Token_without_subject_fails_authentication()
    {
        var validator = Substitute.For<IUserSessionValidator>();
        var context = CreateContext(validator, new Claim("sv", "v1"));

        await UserSessionTokenValidation.OnTokenValidatedAsync(context);

        context.Result!.Failure.ShouldNotBeNull();
        await validator.DidNotReceiveWithAnyArgs().IsValidAsync(default, default, TestContext.Current.CancellationToken);
    }
}

public sealed class DummyPasswordVerifierTests
{
    /// <summary>Haqiqiy hasher ustidagi hisoblagich (alohida tur — soxta xesh keshi boshqa testlar bilan aralashmaydi).</summary>
    private sealed class CountingHasher : IPasswordHasher<ApplicationUser>
    {
        private readonly PasswordHasher<ApplicationUser> _inner = new();

        public int HashCalls { get; private set; }
        public int VerifyCalls { get; private set; }
        public PasswordVerificationResult LastResult { get; private set; }

        public string HashPassword(ApplicationUser user, string password)
        {
            HashCalls++;
            return _inner.HashPassword(user, password);
        }

        public PasswordVerificationResult VerifyHashedPassword(ApplicationUser user, string hashedPassword, string providedPassword)
        {
            VerifyCalls++;
            LastResult = _inner.VerifyHashedPassword(user, hashedPassword, providedPassword);
            return LastResult;
        }
    }

    [Fact]
    public void Performs_a_full_hash_verification_and_reuses_the_precomputed_hash()
    {
        var hasher = new CountingHasher();
        var verifier = new DummyPasswordVerifier(hasher);

        verifier.Verify("Secret123");
        verifier.Verify("Other456");
        new DummyPasswordVerifier(hasher).Verify("Third789");

        hasher.VerifyCalls.ShouldBe(3);
        hasher.HashCalls.ShouldBe(1);
        hasher.LastResult.ShouldBe(PasswordVerificationResult.Failed);
    }
}
