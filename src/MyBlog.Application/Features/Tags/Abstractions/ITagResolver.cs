namespace MyBlog.Application.Features.Tags.Abstractions;

/// <summary>Teg nomlarini egasining teg id'lariga aylantiradi (implementatsiyasi Tags modulida).</summary>
public interface ITagResolver
{
    Task<IReadOnlyList<Guid>> ResolveAsync(Guid ownerId, IEnumerable<string> names, CancellationToken cancellationToken = default);
}
