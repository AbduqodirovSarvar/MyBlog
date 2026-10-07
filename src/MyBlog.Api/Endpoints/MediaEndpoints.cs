using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Net.Http.Headers;
using MyBlog.Application.Abstractions.Services;

namespace MyBlog.Api.Endpoints;

/// <summary>
/// Media fayllarini tarqatish: <c>GET /media/{**path}</c> → IFileStorage. Fayl nomlari unikal (guid) va o'zgarmas,
/// shuning uchun bir yillik immutable kesh va ETag (kalitdan) ishlatiladi. Anonim.
/// <para>
/// DataIsolation:PublicReadOfPublishedContent=false bu yerda ataylab qo'llanmaydi: kalitlar taxmin qilib bo'lmaydigan
/// tasodifiy GUID'lar (capability URL) va ular faqat allaqachon yopilgan endpoint'lar orqali oshkor bo'ladi. Egasini
/// tekshirish har so'rovda bazadan kalit (va variantlar) bo'yicha qidirishni talab qiladi hamda public/immutable keshni
/// buzadi. Kerak bo'lsa keyinchalik: signed URL yoki private storage.
/// </para>
/// </summary>
internal static class MediaEndpoints
{
    private const string CacheControlValue = "public, max-age=31536000, immutable";

    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".gif"
    };

    public static IEndpointRouteBuilder MapMediaEndpoints(this IEndpointRouteBuilder app)
    {
        var configuration = app.ServiceProvider.GetRequiredService<IConfiguration>();
        var basePath = "/" + (configuration["Storage:PublicBasePath"] ?? "/media").Trim('/');

        app.MapMethods(basePath + "/{**path}", [HttpMethods.Get, HttpMethods.Head], ServeAsync)
            .AllowAnonymous()
            .DisableRateLimiting()
            .ExcludeFromDescription();

        return app;
    }

    private static async Task<IResult> ServeAsync(string? path, HttpContext context, IFileStorage storage, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path)
            || !AllowedExtensions.Contains(Path.GetExtension(path))
            || !ContentTypes.TryGetContentType(path, out var contentType))
            return Results.NotFound();

        Stream? stream;
        try
        {
            stream = await storage.OpenReadAsync(path, cancellationToken);
        }
        catch (ArgumentException)
        {
            // Storage xavfli kalitni (.., absolyut yo'l) rad etdi.
            return Results.NotFound();
        }

        if (stream is null)
            return Results.NotFound();

        var headers = context.Response.Headers;
        headers.CacheControl = CacheControlValue;
        headers.XContentTypeOptions = "nosniff";

        // Kalit o'zgarmas fayl — ETag kalitdan; If-None-Match'ni Results.Stream o'zi 304 ga aylantiradi.
        var etag = new EntityTagHeaderValue($"\"{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(path)))[..32]}\"");
        return Results.Stream(stream, contentType, entityTag: etag, enableRangeProcessing: true);
    }
}
