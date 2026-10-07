using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Application.Features.Tags.Abstractions;
using MyBlog.Application.Features.Tags.Common;
using MyBlog.Domain.Tags;

namespace MyBlog.Application.Features.Tags;

internal sealed class TagResolver(IRepository<Tag> tags, ISlugGenerator slugGenerator) : ITagResolver
{
    public async Task<IReadOnlyList<Guid>> ResolveAsync(Guid ownerId, IEnumerable<string> names,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(names);
        if (ownerId == Guid.Empty)
            throw new ArgumentException("Owner id is required.", nameof(ownerId));

        var requested = Normalize(names);
        if (requested.Count == 0)
            return [];

        var existing = await tags.ListAsync(new TagsBySlugsSpec(ownerId, requested.Select(r => r.Slug).ToList()),
            cancellationToken);
        var idsBySlug = existing.ToDictionary(t => t.Slug, t => t.Id, StringComparer.Ordinal);

        var result = new List<Guid>(requested.Count);
        foreach (var (name, slug) in requested)
        {
            if (idsBySlug.TryGetValue(slug, out var id))
            {
                result.Add(id);
                continue;
            }

            var created = Tag.Create(ownerId, name, slug);
            if (created.IsFailure)
                continue;

            tags.Add(created.Value);
            idsBySlug[slug] = created.Value.Id;
            result.Add(created.Value.Id);
        }

        return result;
    }

    /// <summary>Trim, bo'shlarini tashlash, uzunlikni cheklash, slug bo'yicha dedupe va limit.</summary>
    internal List<(string Name, string Slug)> Normalize(IEnumerable<string> names)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<(string, string)>();

        foreach (var raw in names)
        {
            var name = raw?.Trim();
            if (string.IsNullOrEmpty(name))
                continue;
            if (name.Length > TagConstraints.NameMaxLength)
                name = name[..TagConstraints.NameMaxLength].TrimEnd();

            var slug = slugGenerator.Generate(name, TagConstraints.SlugMaxLength);
            if (!seen.Add(slug))
                continue;

            result.Add((name, slug));
            if (result.Count == ITagResolver.MaxTags)
                break;
        }

        return result;
    }
}
