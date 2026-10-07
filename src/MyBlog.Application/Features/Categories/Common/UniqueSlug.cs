namespace MyBlog.Application.Features.Categories.Common;

/// <summary>Band slug'lar ichida unikal variant: "news" → "news-2" → "news-3" ...</summary>
internal static class UniqueSlug
{
    public static string Resolve(string baseSlug, IReadOnlyCollection<string> taken, int maxLength)
    {
        ArgumentException.ThrowIfNullOrEmpty(baseSlug);

        var takenSet = taken as ISet<string> ?? new HashSet<string>(taken, StringComparer.Ordinal);
        if (!takenSet.Contains(baseSlug))
            return baseSlug;

        for (var n = 2; ; n++)
        {
            var suffix = $"-{n}";
            var head = baseSlug.Length + suffix.Length > maxLength
                ? baseSlug[..Math.Max(1, maxLength - suffix.Length)].TrimEnd('-')
                : baseSlug;

            var candidate = head + suffix;
            if (!takenSet.Contains(candidate))
                return candidate;
        }
    }
}
