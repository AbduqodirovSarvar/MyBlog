using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyBlog.Application.Abstractions.Identity;
using MyBlog.Application.Abstractions.Services;

namespace MyBlog.Application.Features.Auth.Common;

internal static class AuthEmailTemplates
{
    public const string ConfirmEmail = "confirm-email";
    public const string ResetPassword = "reset-password";
    public const string PasswordChanged = "password-changed";
    public const string Welcome = "welcome";
}

/// <summary>
/// Auth email'lari. Til — foydalanuvchi profilidagi PreferredCulture, bo'lmasa joriy so'rov tili.
/// Email'lar navbatga qo'yiladi (so'rov SMTP'ni kutmaydi).
/// </summary>
internal sealed class AuthEmailService(
    IIdentityService identityService,
    IEmailTemplateRenderer renderer,
    IEmailQueue emailQueue,
    ILocalizer localizer,
    UserProfileReader profileReader,
    IOptions<FrontendOptions> frontendOptions,
    IOptions<AuthOptions> authOptions,
    TimeProvider timeProvider,
    ILogger<AuthEmailService> logger)
{
    public async Task SendEmailConfirmationAsync(AuthUser user, CancellationToken cancellationToken)
    {
        var culture = await ResolveCultureAsync(user.Id, cancellationToken);
        var token = await identityService.GenerateEmailConfirmationTokenAsync(user.Id, cancellationToken);
        var link = BuildLink(frontendOptions.Value.ConfirmEmailPath, culture,
            ("userId", user.Id.ToString()), ("token", token));

        await SendAsync(AuthEmailTemplates.ConfirmEmail, culture, user, new Dictionary<string, string>
        {
            ["link"] = link,
            ["hours"] = authOptions.Value.EmailTokenLifetimeHours.ToString(CultureInfo.InvariantCulture)
        }, cancellationToken);
    }

    public async Task SendPasswordResetAsync(AuthUser user, CancellationToken cancellationToken)
    {
        var culture = await ResolveCultureAsync(user.Id, cancellationToken);
        var token = await identityService.GeneratePasswordResetTokenAsync(user.Id, cancellationToken);
        var link = BuildLink(frontendOptions.Value.ResetPasswordPath, culture,
            ("email", user.Email), ("token", token));

        await SendAsync(AuthEmailTemplates.ResetPassword, culture, user, new Dictionary<string, string>
        {
            ["link"] = link,
            ["minutes"] = authOptions.Value.PasswordResetTokenLifetimeMinutes.ToString(CultureInfo.InvariantCulture)
        }, cancellationToken);
    }

    public async Task SendPasswordChangedAsync(AuthUser user, CancellationToken cancellationToken)
    {
        var culture = await ResolveCultureAsync(user.Id, cancellationToken);
        await SendAsync(AuthEmailTemplates.PasswordChanged, culture, user, new Dictionary<string, string>
        {
            ["time"] = timeProvider.GetUtcNow().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture),
            ["link"] = BuildLink("/{culture}", culture)
        }, cancellationToken);
    }

    public async Task SendWelcomeAsync(AuthUser user, CancellationToken cancellationToken)
    {
        var culture = await ResolveCultureAsync(user.Id, cancellationToken);
        await SendAsync(AuthEmailTemplates.Welcome, culture, user, new Dictionary<string, string>
        {
            ["link"] = BuildLink("/{culture}", culture)
        }, cancellationToken);
    }

    private async Task SendAsync(string template, string culture, AuthUser user, Dictionary<string, string> values,
        CancellationToken cancellationToken)
    {
        values["userName"] = user.UserName;

        var rendered = await renderer.RenderAsync(template, culture, values, cancellationToken);
        await emailQueue.EnqueueAsync(new EmailMessage(user.Email, rendered.Subject, rendered.HtmlBody, rendered.TextBody),
            cancellationToken);

        logger.LogInformation("Auth email {Template} queued for user {UserId}", template, user.Id);
    }

    private async Task<string> ResolveCultureAsync(Guid userId, CancellationToken cancellationToken)
    {
        var summary = await profileReader.GetSummaryAsync(userId, cancellationToken);
        return localizer.NormalizeCulture(summary?.PreferredCulture ?? localizer.CurrentCulture);
    }

    /// <summary>Frontend havolasi: BaseUrl + yo'l ("{culture}" almashtiriladi) + query.</summary>
    internal string BuildLink(string path, string culture, params (string Key, string Value)[] query)
    {
        var baseUrl = frontendOptions.Value.BaseUrl.TrimEnd('/');
        var relative = path.Replace("{culture}", culture, StringComparison.OrdinalIgnoreCase);
        if (!relative.StartsWith('/'))
            relative = "/" + relative;

        if (query.Length == 0)
            return baseUrl + relative;

        var queryString = string.Join('&', query.Select(q => $"{Uri.EscapeDataString(q.Key)}={Uri.EscapeDataString(q.Value)}"));
        return $"{baseUrl}{relative}{(relative.Contains('?', StringComparison.Ordinal) ? '&' : '?')}{queryString}";
    }
}
