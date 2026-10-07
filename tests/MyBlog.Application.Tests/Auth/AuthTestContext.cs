using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MyBlog.Application.Abstractions.Authorization;
using MyBlog.Application.Abstractions.Identity;
using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Application.Features.Auth;
using MyBlog.Application.Features.Auth.Common;
using MyBlog.Domain.Common;
using MyBlog.Domain.Media;
using MyBlog.Domain.Users;
using NSubstitute;

namespace MyBlog.Application.Tests.Auth;

/// <summary>Auth handler testlari uchun umumiy mock'lar va haqiqiy yordamchi servislar.</summary>
internal sealed class AuthTestContext
{
    public static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    public AuthTestContext()
    {
        Localizer.CurrentCulture.Returns("ru");
        Localizer.DefaultCulture.Returns("uz");
        Localizer.NormalizeCulture(Arg.Any<string?>()).Returns(ci => ci.Arg<string?>() ?? "uz");
        Localizer.Get(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<object[]>()).Returns(ci => ci.ArgAt<string>(0));
        Localizer.Find(Arg.Any<string>(), Arg.Any<string?>()).Returns((string?)null);

        Renderer.RenderAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult(new RenderedEmail(ci.ArgAt<string>(0), "<p>html</p>", "text")));

        Profiles.FirstOrDefaultAsync(Arg.Any<ISpecification<UserProfile, ProfileSummary>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ProfileSummary?>(null));

        Tokens.CreateAccessToken(Arg.Any<AuthUser>())
            .Returns(new AccessToken("access-token", Now.AddMinutes(15)));
        RefreshTokens.IssueAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new IssuedRefreshToken("refresh-token", Now.AddDays(14))));

        Time.GetUtcNow().Returns(Now);

        ProfileReader = new UserProfileReader(Profiles, Media, FileStorage);
        SessionIssuer = new AuthSessionIssuer(Tokens, RefreshTokens, ProfileReader);
        EmailService = new AuthEmailService(Identity, Renderer, EmailQueue, Localizer, ProfileReader,
            Options.Create(Frontend), Options.Create(AuthOptions), Time, NullLogger<AuthEmailService>.Instance);
    }

    public IIdentityService Identity { get; } = Substitute.For<IIdentityService>();
    public ITokenService Tokens { get; } = Substitute.For<ITokenService>();
    public IRefreshTokenService RefreshTokens { get; } = Substitute.For<IRefreshTokenService>();
    public IRepository<UserProfile> Profiles { get; } = Substitute.For<IRepository<UserProfile>>();
    public IReadRepository<MediaFile> Media { get; } = Substitute.For<IReadRepository<MediaFile>>();
    public IFileStorage FileStorage { get; } = Substitute.For<IFileStorage>();
    public IEmailTemplateRenderer Renderer { get; } = Substitute.For<IEmailTemplateRenderer>();
    public IEmailQueue EmailQueue { get; } = Substitute.For<IEmailQueue>();
    public ILocalizer Localizer { get; } = Substitute.For<ILocalizer>();
    public ICurrentUser CurrentUser { get; } = Substitute.For<ICurrentUser>();
    public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
    public TimeProvider Time { get; } = Substitute.For<TimeProvider>();

    public FrontendOptions Frontend { get; } = new()
    {
        BaseUrl = "https://myblog.uz/",
        ConfirmEmailPath = "/{culture}/auth/confirm-email",
        ResetPasswordPath = "/{culture}/auth/reset-password"
    };

    public AuthOptions AuthOptions { get; } = new() { RequireConfirmedEmail = true };

    public UserProfileReader ProfileReader { get; }
    public AuthSessionIssuer SessionIssuer { get; }
    public AuthEmailService EmailService { get; }

    public static AuthUser User(Guid? id = null, bool emailConfirmed = true, bool isBlocked = false, params string[] roles) =>
        new(id ?? Guid.CreateVersion7(), "ali@example.com", "ali", emailConfirmed, isBlocked,
            roles.Length == 0 ? [Roles.User] : roles, [Permissions.Posts.Manage]);

    /// <summary>ExecuteInTransactionAsync delegatni darhol bajaradi (tranzaksiyasiz).</summary>
    public void RunTransactionsInline<TResult>() =>
        UnitOfWork.ExecuteInTransactionAsync(Arg.Any<Func<CancellationToken, Task<TResult>>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<Func<CancellationToken, Task<TResult>>>()(CancellationToken.None));

    /// <summary>Navbatga qo'yilgan email'lar (Subject = shablon nomi, chunki renderer mock'i shunday qaytaradi).</summary>
    public List<EmailMessage> QueuedEmails() =>
        EmailQueue.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IEmailQueue.EnqueueAsync))
            .Select(c => (EmailMessage)c.GetArguments()[0]!)
            .ToList();

    /// <summary>RequiredId default interface member — NSubstitute uni alohida stub qilishi kerak.</summary>
    public void SignIn(Guid userId)
    {
        CurrentUser.Id.Returns(userId);
        CurrentUser.RequiredId.Returns(userId);
        CurrentUser.IsAuthenticated.Returns(true);
    }
}
