using MyBlog.Application.Abstractions.Services;
using MyBlog.Application.Features.Profile.Common;
using NSubstitute;

namespace MyBlog.Application.Tests.Features;

// Internal interfeyslar uchun qo'lda yozilgan fake'lar (NSubstitute internal tiplarni proksi qila olmaydi).

internal sealed class FakeAuthorCache : IAuthorCacheInvalidator
{
    public List<string> Invalidated { get; } = [];
    public int CurrentUserInvalidations { get; private set; }

    public Task InvalidateAsync(string username, CancellationToken cancellationToken = default)
    {
        Invalidated.Add(username);
        return Task.CompletedTask;
    }

    public Task InvalidateCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        CurrentUserInvalidations++;
        return Task.CompletedTask;
    }
}

internal sealed class FakeMediaUrlResolver : IMediaUrlResolver
{
    public Task<IReadOnlyDictionary<Guid, string>> ResolveAsync(IEnumerable<Guid?> mediaIds, bool publicAccess,
        string? preferredVariant = null, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(mediaIds
            .Where(id => id is not null)
            .Distinct()
            .ToDictionary(id => id!.Value, id => $"/media/{id}"));
}

/// <summary>Soddalashtirilgan slug: kichik harf, harf/raqam bo'lmaganlar "-".</summary>
internal sealed class SimpleSlugGenerator : ISlugGenerator
{
    public string Generate(string text, int maxLength = 120)
    {
        var chars = text.Trim().ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray();
        var slug = string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
        return slug.Length > maxLength ? slug[..maxLength].TrimEnd('-') : slug;
    }
}

internal static class TestUsers
{
    public static ICurrentUser Authenticated(Guid id)
    {
        var user = Substitute.For<ICurrentUser>();
        user.Id.Returns(id);
        user.RequiredId.Returns(id);
        user.IsAuthenticated.Returns(true);
        return user;
    }
}
