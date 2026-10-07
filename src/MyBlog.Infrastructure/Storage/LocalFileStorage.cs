using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MyBlog.Application.Abstractions.Services;

namespace MyBlog.Infrastructure.Storage;

internal sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Fayllar papkasi; nisbiy bo'lsa ContentRootPath'ga nisbatan.</summary>
    [Required]
    public string RootPath { get; set; } = "storage/media";

    /// <summary>Public URL prefiksi (static files middleware shu yo'lga ulanadi).</summary>
    [Required]
    public string PublicBasePath { get; set; } = "/media";
}

/// <summary>
/// Local diskka saqlash. Kalit — "/" bilan ajratilgan nisbiy yo'l (masalan 2026/10/abc.webp).
/// Path traversal'dan himoyalangan; yozish vaqtinchalik fayl + atomik move orqali.
/// </summary>
internal sealed class LocalFileStorage : IFileStorage
{
    private readonly string _publicBasePath;

    public LocalFileStorage(IOptions<StorageOptions> options, IHostEnvironment environment)
    {
        var root = options.Value.RootPath;
        RootPath = Path.GetFullPath(Path.IsPathRooted(root) ? root : Path.Combine(environment.ContentRootPath, root));
        _publicBasePath = "/" + options.Value.PublicBasePath.Trim('/');
    }

    /// <summary>Saqlash papkasining to'liq yo'li (static files uchun).</summary>
    public string RootPath { get; }

    public async Task<string> SaveAsync(Stream content, string key, string contentType, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var normalizedKey = NormalizeKey(key);
        var fullPath = ResolvePath(normalizedKey);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        var tempPath = $"{fullPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var target = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             bufferSize: 81920, useAsync: true))
            {
                await content.CopyToAsync(target, cancellationToken);
            }

            File.Move(tempPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }

        return normalizedKey;
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        var fullPath = ResolvePath(NormalizeKey(key));
        Stream? stream = File.Exists(fullPath)
            ? new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920, useAsync: true)
            : null;
        return Task.FromResult(stream);
    }

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(File.Exists(ResolvePath(NormalizeKey(key))));

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var fullPath = ResolvePath(NormalizeKey(key));
        if (File.Exists(fullPath))
            File.Delete(fullPath);
        return Task.CompletedTask;
    }

    public string GetPublicUrl(string key) =>
        $"{_publicBasePath}/{string.Join('/', NormalizeKey(key).Split('/').Select(Uri.EscapeDataString))}";

    /// <summary>Kalitni tekshiradi va "/" ko'rinishiga keltiradi. Xavfli kalitlarda ArgumentException.</summary>
    internal static string NormalizeKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Storage key is required.", nameof(key));

        var normalized = key.Trim().Replace('\\', '/');

        if (normalized.StartsWith('/') || normalized.Contains(':') || Path.IsPathRooted(normalized))
            throw new ArgumentException("Storage key must be a relative path.", nameof(key));

        var segments = normalized.Split('/');
        if (segments.Any(s => s.Length == 0 || s == "." || s == ".." || s.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            throw new ArgumentException("Storage key contains an invalid path segment.", nameof(key));

        return normalized;
    }

    private string ResolvePath(string normalizedKey)
    {
        var fullPath = Path.GetFullPath(Path.Combine(RootPath, normalizedKey));
        var rootWithSeparator = RootPath.EndsWith(Path.DirectorySeparatorChar) ? RootPath : RootPath + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.Ordinal))
            throw new ArgumentException("Storage key resolves outside of the storage root.", nameof(normalizedKey));

        return fullPath;
    }
}
