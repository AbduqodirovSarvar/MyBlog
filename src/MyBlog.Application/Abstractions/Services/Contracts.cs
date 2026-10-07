namespace MyBlog.Application.Abstractions.Services;

/// <summary>So'rovni yuborgan joriy foydalanuvchi (Api qatlamida HttpContext orqali implementatsiya qilinadi).</summary>
public interface ICurrentUser
{
    Guid? Id { get; }
    bool IsAuthenticated { get; }
    string? UserName { get; }
    string? Email { get; }
    IReadOnlyCollection<string> Roles { get; }

    bool IsInRole(string role);
    bool HasPermission(string permission);

    /// <summary>Autentifikatsiya talab qilinadigan joylarda: Id bo'lmasa exception.</summary>
    Guid RequiredId => Id ?? throw new UnauthorizedAccessException("User is not authenticated.");
}

/// <summary>Xabarlarni joriy UI tiliga tarjima qilish.</summary>
public interface ILocalizer
{
    /// <summary>Joriy so'rov tili (masalan "uz", "ru").</summary>
    string CurrentCulture { get; }

    string DefaultCulture { get; }

    IReadOnlyList<string> SupportedCultures { get; }

    /// <summary>Kalit bo'yicha tarjima. Topilmasa null.</summary>
    string? Find(string key, string? culture = null);

    /// <summary>Kalit bo'yicha tarjima; topilmasa <paramref name="fallback"/> yoki kalitning o'zi.</summary>
    string Get(string key, string? fallback = null, params object[] args);

    /// <summary>Berilgan til qo'llab-quvvatlanadimi; bo'lmasa default tilni qaytaradi.</summary>
    string NormalizeCulture(string? culture);
}

/// <summary>URL uchun slug yaratish (kirill va o'zbek harflarini lotinga o'giradi).</summary>
public interface ISlugGenerator
{
    string Generate(string text, int maxLength = 120);
}

/// <summary>HTML'ni whitelist asosida tozalash (XSS himoyasi).</summary>
public interface IHtmlSanitizer
{
    string Sanitize(string html);
}

/// <summary>Fayllarni saqlash abstraksiyasi. Hozir local disk, keyin S3/Blob qo'shish mumkin.</summary>
public interface IFileStorage
{
    Task<string> SaveAsync(Stream content, string key, string contentType, CancellationToken cancellationToken = default);
    Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Kalit uchun public URL (masalan /media/2026/10/abc.webp).</summary>
    string GetPublicUrl(string key);
}

public interface ICacheService
{
    Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, TimeSpan? expiration = null,
        IEnumerable<string>? tags = null, CancellationToken cancellationToken = default);

    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
    Task RemoveByTagAsync(string tag, CancellationToken cancellationToken = default);
}

public sealed record EmailMessage(string To, string Subject, string HtmlBody, string? TextBody = null);

/// <summary>Email transporti (SMTP va h.k.).</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

/// <summary>Emailni fonda yuborish uchun navbatga qo'yadi (so'rov email serverini kutmaydi).</summary>
public interface IEmailQueue
{
    ValueTask EnqueueAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

public sealed record RenderedEmail(string Subject, string HtmlBody, string TextBody);

/// <summary>Email shablonlarini kerakli tilda render qiladi.</summary>
public interface IEmailTemplateRenderer
{
    Task<RenderedEmail> RenderAsync(string templateName, string culture, IReadOnlyDictionary<string, string> values,
        CancellationToken cancellationToken = default);
}

/// <summary>Davriy ishlaydigan fon vazifasi. Runner Infrastructure'da, logika Application'da.</summary>
public interface IRecurringJob
{
    string Name { get; }
    TimeSpan Interval { get; }
    Task ExecuteAsync(CancellationToken cancellationToken);
}
