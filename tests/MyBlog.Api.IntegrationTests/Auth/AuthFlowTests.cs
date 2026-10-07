using System.Net;
using System.Net.Http.Json;
using MyBlog.Api.IntegrationTests.Infrastructure;
using MyBlog.Application.Abstractions.Authorization;
using MyBlog.Application.Features.Auth.Common;
using MyBlog.Application.Features.Auth.Register;

namespace MyBlog.Api.IntegrationTests.Auth;

[Collection(PostgresCollection.Name)]
public sealed class AuthFlowTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Register_confirm_login_refresh_logout_flow()
    {
        postgres.SkipIfUnavailable();
        await using var host = await AuthTestHost.CreateAsync(postgres);

        // Ro'yxatdan o'tish
        var register = await host.PostAsync("/api/auth/register", new
        {
            email = "ali@example.com",
            userName = "ali",
            password = AuthTestHost.Password,
            confirmPassword = AuthTestHost.Password,
            firstName = "Ali",
            culture = "en"
        });
        register.StatusCode.ShouldBe(HttpStatusCode.OK, await register.Content.ReadAsStringAsync(Ct));
        var registered = (await register.Content.ReadFromJsonAsync<RegisterResponse>(Ct))!;
        registered.RequiresEmailConfirmation.ShouldBeTrue();

        // Tasdiqlanmagan email bilan kirib bo'lmaydi
        var early = await host.PostAsync("/api/auth/login", new { emailOrUserName = "ali", password = AuthTestHost.Password });
        early.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await AuthTestHost.ErrorCodeAsync(early)).ShouldBe("Auth.EmailNotConfirmed");

        // Email'dagi havola orqali tasdiqlash
        var query = host.Emails.LinkQuery("ali@example.com", "/en/auth/confirm-email");
        query["userId"].ShouldBe(registered.UserId.ToString());
        (await host.PostAsync("/api/auth/confirm-email", new { userId = registered.UserId, token = query["token"] }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        host.Emails.Messages.Count(m => m.To == "ali@example.com").ShouldBe(2); // confirm + welcome

        // Kirish
        var session = await host.LoginAsync("ali");
        session.User.UserName.ShouldBe("ali");
        session.User.DisplayName.ShouldBe("Ali");
        session.User.Roles.ShouldBe([Roles.User]);
        session.User.Permissions.ShouldContain(Permissions.Posts.Manage);

        var me = await host.GetAsync("/api/auth/me", session.AccessToken);
        me.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await me.Content.ReadFromJsonAsync<CurrentUserResponse>(Ct))!.Id.ShouldBe(registered.UserId);
        (await host.GetAsync("/api/auth/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // Rotatsiya
        var refresh = await host.PostAsync("/api/auth/refresh", new { refreshToken = session.RefreshToken });
        refresh.StatusCode.ShouldBe(HttpStatusCode.OK);
        var rotated = (await refresh.Content.ReadFromJsonAsync<AuthResponse>(Ct))!;
        rotated.RefreshToken.ShouldNotBe(session.RefreshToken);

        // Eski token qayta ishlatildi → reuse: butun oila bekor qilinadi
        (await host.PostAsync("/api/auth/refresh", new { refreshToken = session.RefreshToken }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await host.PostAsync("/api/auth/refresh", new { refreshToken = rotated.RefreshToken }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // Logout
        var second = await host.LoginAsync("ali@example.com");
        (await host.PostAsync("/api/auth/logout", new { refreshToken = second.RefreshToken }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await host.PostAsync("/api/auth/refresh", new { refreshToken = second.RefreshToken }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Forgot_and_reset_password_flow_revokes_sessions()
    {
        postgres.SkipIfUnavailable();
        await using var host = await AuthTestHost.CreateAsync(postgres);
        await host.RegisterConfirmedAsync("vali");
        var session = await host.LoginAsync("vali");

        var known = await host.PostAsync("/api/auth/forgot-password", new { email = "vali@example.com" });
        var unknown = await host.PostAsync("/api/auth/forgot-password", new { email = "nobody@example.com" });
        known.StatusCode.ShouldBe(HttpStatusCode.OK);
        unknown.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await unknown.Content.ReadAsStringAsync(Ct)).ShouldBe(await known.Content.ReadAsStringAsync(Ct));
        host.Emails.Messages.ShouldNotContain(m => m.To == "nobody@example.com");

        var query = host.Emails.LinkQuery("vali@example.com", "/en/auth/reset-password");
        query["email"].ShouldBe("vali@example.com");

        var reset = await host.PostAsync("/api/auth/reset-password", new
        {
            email = "vali@example.com",
            token = query["token"],
            newPassword = "NewSecret456",
            confirmPassword = "NewSecret456"
        });
        reset.StatusCode.ShouldBe(HttpStatusCode.NoContent, await reset.Content.ReadAsStringAsync(Ct));

        // Eski sessiya va eski parol ishlamaydi
        (await host.PostAsync("/api/auth/refresh", new { refreshToken = session.RefreshToken }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var oldPassword = await host.PostAsync("/api/auth/login", new { emailOrUserName = "vali", password = AuthTestHost.Password });
        oldPassword.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await AuthTestHost.ErrorCodeAsync(oldPassword)).ShouldBe("Auth.InvalidCredentials");

        await host.LoginAsync("vali", "NewSecret456");

        // Token bir martalik
        var reused = await host.PostAsync("/api/auth/reset-password", new
        {
            email = "vali@example.com",
            token = query["token"],
            newPassword = "Another789X",
            confirmPassword = "Another789X"
        });
        reused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Admin_endpoints_require_permissions_and_blocking_stops_login()
    {
        postgres.SkipIfUnavailable();
        await using var host = await AuthTestHost.CreateAsync(postgres);
        var userId = await host.RegisterConfirmedAsync("karim");
        var user = await host.LoginAsync("karim");

        (await host.GetAsync("/api/admin/users", user.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Seed qilingan SuperAdmin (appsettings.json: Seed:SuperAdmin)
        var admin = await host.LoginAsync("superadmin", "ChangeMe123!");
        admin.User.Roles.ShouldContain(Roles.SuperAdmin);

        var list = await host.GetAsync("/api/admin/users?search=karim", admin.AccessToken);
        list.StatusCode.ShouldBe(HttpStatusCode.OK, await list.Content.ReadAsStringAsync(Ct));
        (await list.Content.ReadAsStringAsync(Ct)).ShouldContain("\"totalCount\":1");

        (await host.PostAsync($"/api/admin/users/{userId}/block", new { }, admin.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var blocked = await host.PostAsync("/api/auth/login", new { emailOrUserName = "karim", password = AuthTestHost.Password });
        blocked.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await AuthTestHost.ErrorCodeAsync(blocked)).ShouldBe("Auth.UserBlocked");
        (await host.PostAsync("/api/auth/refresh", new { refreshToken = user.RefreshToken }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var selfBlock = await host.PostAsync($"/api/admin/users/{admin.User.Id}/block", new { }, admin.AccessToken);
        selfBlock.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
