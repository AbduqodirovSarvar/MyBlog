using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyBlog.Api.IntegrationTests.Infrastructure;
using MyBlog.Application.Abstractions.Authorization;
using MyBlog.Application.Features.Auth.Common;
using MyBlog.Infrastructure.Identity;

namespace MyBlog.Api.IntegrationTests.Auth;

/// <summary>Access token security stamp'ga bog'langan: stamp o'zgarsa eski token keyingi so'rovdayoq 401 oladi.</summary>
[Collection(PostgresCollection.Name)]
public sealed class SessionRevocationTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task ShouldBeUnauthorizedAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await AuthTestHost.ErrorCodeAsync(response)).ShouldBe("General.Unauthorized");
    }

    [Fact]
    public async Task Blocking_rejects_existing_access_token_immediately_and_unblocked_user_can_log_in_again()
    {
        postgres.SkipIfUnavailable();
        await using var host = await AuthTestHost.CreateAsync(postgres);
        var userId = await host.RegisterConfirmedAsync("bobur");
        var user = await host.LoginAsync("bobur");
        var admin = await host.LoginAsync("superadmin", "ChangeMe123!");

        // Kesh to'ldiriladi — bloklashdan keyin eskirgan kesh qiymati ishlatilmasligi tekshiriladi.
        (await host.GetAsync("/api/auth/me", user.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await host.PostAsync($"/api/admin/users/{userId}/block", new { }, admin.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await ShouldBeUnauthorizedAsync(await host.GetAsync("/api/auth/me", user.AccessToken));
        (await host.PostAsync("/api/auth/refresh", new { refreshToken = user.RefreshToken }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // Admin'ning o'z token'i ta'sirlanmaydi.
        (await host.GetAsync("/api/auth/me", admin.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await host.PostAsync($"/api/admin/users/{userId}/unblock", new { }, admin.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Blokdan chiqarish eski token'ni tiriltirmaydi, lekin qayta kirish mumkin.
        await ShouldBeUnauthorizedAsync(await host.GetAsync("/api/auth/me", user.AccessToken));
        var again = await host.LoginAsync("bobur");
        (await host.GetAsync("/api/auth/me", again.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Password_reset_rejects_old_access_token()
    {
        postgres.SkipIfUnavailable();
        await using var host = await AuthTestHost.CreateAsync(postgres);
        await host.RegisterConfirmedAsync("dilnoza");
        var session = await host.LoginAsync("dilnoza");
        (await host.GetAsync("/api/auth/me", session.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await host.PostAsync("/api/auth/forgot-password", new { email = "dilnoza@example.com" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        var query = host.Emails.LinkQuery("dilnoza@example.com", "/en/auth/reset-password");
        var reset = await host.PostAsync("/api/auth/reset-password", new
        {
            email = "dilnoza@example.com",
            token = query["token"],
            newPassword = "NewSecret456",
            confirmPassword = "NewSecret456"
        });
        reset.StatusCode.ShouldBe(HttpStatusCode.NoContent, await reset.Content.ReadAsStringAsync(Ct));

        await ShouldBeUnauthorizedAsync(await host.GetAsync("/api/auth/me", session.AccessToken));

        var fresh = await host.LoginAsync("dilnoza", "NewSecret456");
        (await host.GetAsync("/api/auth/me", fresh.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Change_password_rejects_old_access_token_and_returns_working_new_one()
    {
        postgres.SkipIfUnavailable();
        await using var host = await AuthTestHost.CreateAsync(postgres);
        await host.RegisterConfirmedAsync("jasur");
        var session = await host.LoginAsync("jasur");
        (await host.GetAsync("/api/auth/me", session.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var change = await host.PostAsync("/api/auth/change-password", new
        {
            currentPassword = AuthTestHost.Password,
            newPassword = "NewSecret456",
            confirmPassword = "NewSecret456"
        }, session.AccessToken);
        change.StatusCode.ShouldBe(HttpStatusCode.OK, await change.Content.ReadAsStringAsync(Ct));
        var changed = (await change.Content.ReadFromJsonAsync<AuthResponse>(Ct))!;

        await ShouldBeUnauthorizedAsync(await host.GetAsync("/api/auth/me", session.AccessToken));
        (await host.GetAsync("/api/auth/me", changed.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Logout_all_rejects_access_tokens_of_every_session()
    {
        postgres.SkipIfUnavailable();
        await using var host = await AuthTestHost.CreateAsync(postgres);
        await host.RegisterConfirmedAsync("malika");
        var phone = await host.LoginAsync("malika");
        var laptop = await host.LoginAsync("malika");
        (await host.GetAsync("/api/auth/me", phone.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await host.PostAsync("/api/auth/logout-all", new { }, laptop.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await ShouldBeUnauthorizedAsync(await host.GetAsync("/api/auth/me", phone.AccessToken));
        await ShouldBeUnauthorizedAsync(await host.GetAsync("/api/auth/me", laptop.AccessToken));
        (await host.PostAsync("/api/auth/refresh", new { refreshToken = phone.RefreshToken }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var again = await host.LoginAsync("malika");
        (await host.GetAsync("/api/auth/me", again.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Role_change_rejects_old_access_token_and_refresh_returns_new_roles()
    {
        postgres.SkipIfUnavailable();
        await using var host = await AuthTestHost.CreateAsync(postgres);
        var userId = await host.RegisterConfirmedAsync("sardor");
        var user = await host.LoginAsync("sardor");
        var admin = await host.LoginAsync("superadmin", "ChangeMe123!");

        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{userId}/roles/{Roles.Admin}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);
        (await host.Client.SendAsync(request, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await ShouldBeUnauthorizedAsync(await host.GetAsync("/api/auth/me", user.AccessToken));

        var refresh = await host.PostAsync("/api/auth/refresh", new { refreshToken = user.RefreshToken });
        refresh.StatusCode.ShouldBe(HttpStatusCode.OK);
        var refreshed = (await refresh.Content.ReadFromJsonAsync<AuthResponse>(Ct))!;
        refreshed.User.Roles.ShouldContain(Roles.Admin);
        (await host.GetAsync("/api/admin/users", refreshed.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}

/// <summary>Login javobi foydalanuvchi mavjudligini (kod va ish hajmi bo'yicha) oshkor qilmaydi.</summary>
[Collection(PostgresCollection.Name)]
public sealed class LoginEnumerationTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Haqiqiy hasher ustidagi hisoblagich: nechta to'liq xesh tekshiruvi bajarilganini ko'rsatadi.</summary>
    private sealed class CountingPasswordHasher : IPasswordHasher<ApplicationUser>
    {
        private readonly PasswordHasher<ApplicationUser> _inner = new();
        private int _verifications;

        public int Verifications => Volatile.Read(ref _verifications);

        public string HashPassword(ApplicationUser user, string password) => _inner.HashPassword(user, password);

        public PasswordVerificationResult VerifyHashedPassword(ApplicationUser user, string hashedPassword, string providedPassword)
        {
            Interlocked.Increment(ref _verifications);
            return _inner.VerifyHashedPassword(user, hashedPassword, providedPassword);
        }
    }

    private static async Task<(HttpStatusCode Status, string? Code, string Body)> LoginAsync(AuthTestHost host, string login,
        string password)
    {
        var response = await host.PostAsync("/api/auth/login", new { emailOrUserName = login, password });
        // traceId har so'rovda farq qiladi — taqqoslashdan chiqariladi.
        var body = Regex.Replace(await response.Content.ReadAsStringAsync(Ct), "\"traceId\":\"[^\"]*\"", "");
        return (response.StatusCode, await AuthTestHost.ErrorCodeAsync(response), body);
    }

    [Fact]
    public async Task Unknown_user_gets_same_response_as_wrong_password_and_still_verifies_a_hash()
    {
        postgres.SkipIfUnavailable();
        var hasher = new CountingPasswordHasher();
        await using var host = await AuthTestHost.CreateAsync(postgres, services =>
        {
            services.RemoveAll<IPasswordHasher<ApplicationUser>>();
            services.AddSingleton<IPasswordHasher<ApplicationUser>>(hasher);
        });
        await host.RegisterConfirmedAsync("nodir");

        var before = hasher.Verifications;
        var wrongPassword = await LoginAsync(host, "nodir", "WrongSecret1");
        var afterWrong = hasher.Verifications;
        var unknownUser = await LoginAsync(host, "nobody", "WrongSecret1");
        var unknownEmail = await LoginAsync(host, "nobody@example.com", "WrongSecret1");

        wrongPassword.Status.ShouldBe(HttpStatusCode.Unauthorized);
        wrongPassword.Code.ShouldBe("Auth.InvalidCredentials");
        unknownUser.Status.ShouldBe(wrongPassword.Status);
        unknownUser.Code.ShouldBe(wrongPassword.Code);
        unknownEmail.Code.ShouldBe(wrongPassword.Code);
        unknownUser.Body.ShouldBe(wrongPassword.Body);

        // Mavjud va mavjud bo'lmagan foydalanuvchi uchun bir xil ish: bittadan to'liq xesh tekshiruvi.
        (afterWrong - before).ShouldBe(1);
        (hasher.Verifications - afterWrong).ShouldBe(2);
    }

    [Fact]
    public async Task Lockout_blocked_and_unconfirmed_states_are_hidden_behind_wrong_password()
    {
        postgres.SkipIfUnavailable();
        await using var host = await AuthTestHost.CreateAsync(postgres);
        await host.RegisterConfirmedAsync("olim");
        var blockedId = await host.RegisterConfirmedAsync("botir");
        var admin = await host.LoginAsync("superadmin", "ChangeMe123!");

        // Tasdiqlanmagan email: noto'g'ri parolda umumiy xato.
        (await host.PostAsync("/api/auth/register", new
        {
            email = "pending@example.com",
            userName = "pending",
            password = AuthTestHost.Password,
            confirmPassword = AuthTestHost.Password,
            culture = "en"
        })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await LoginAsync(host, "pending", "WrongSecret1")).Code.ShouldBe("Auth.InvalidCredentials");
        (await LoginAsync(host, "pending", AuthTestHost.Password)).Code.ShouldBe("Auth.EmailNotConfirmed");

        // Lockout (5 ta xato): lockout'ni yuzaga keltirgan urinish ham, keyingi to'g'ri parol ham umumiy xato oladi.
        for (var i = 0; i < 5; i++)
            (await LoginAsync(host, "olim", "WrongSecret1")).Code.ShouldBe("Auth.InvalidCredentials");
        (await LoginAsync(host, "olim", AuthTestHost.Password)).Code.ShouldBe("Auth.InvalidCredentials");

        // Bloklangan: noto'g'ri parolda umumiy xato, faqat to'g'ri parolda "bloklangan".
        (await host.PostAsync($"/api/admin/users/{blockedId}/block", new { }, admin.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await LoginAsync(host, "botir", "WrongSecret1")).Code.ShouldBe("Auth.InvalidCredentials");
        (await LoginAsync(host, "botir", AuthTestHost.Password)).Code.ShouldBe("Auth.UserBlocked");
    }
}
