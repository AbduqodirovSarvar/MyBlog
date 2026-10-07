using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Application.Features.Posts.Abstractions;
using MyBlog.Domain.Posts;

namespace MyBlog.Application.Features.Posts.Common;

/// <summary>
/// Reviziya qo'shish va limitlar: Autosave'lardan faqat eng yangi <c>AutosaveKeep</c> tasi, jami esa <c>MaxRevisions</c>
/// tadan oshmaydi (eng eskilari o'chiriladi). O'chirish darhol bajariladi — chaqiruvchi tranzaksiya ichida bo'lishi kerak.
/// </summary>
internal static class PostRevisionWriter
{
    /// <param name="state">Berilsa (autosave) shu title/kontent saqlanadi, aks holda post'ning joriy holati.</param>
    /// <param name="clearAutosaves">Qo'lda saqlashda eski autosave'lar keraksiz — o'chiriladi.</param>
    public static async Task<PostRevision> AddAsync(
        IRepository<PostRevision> revisions,
        IPostRepository posts,
        PostsOptions options,
        Post post,
        RevisionKind kind,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        (string Title, PostContent Content)? state = null,
        bool clearAutosaves = false)
    {
        var existing = await revisions.ListAsync(new RevisionHeadersSpec(post.Id), cancellationToken);
        var number = existing.Count == 0 ? 1 : existing.Max(r => r.Number) + 1;

        var revision = state is { } s
            ? PostRevision.Create(post, kind, number, s.Title, s.Content, now)
            : PostRevision.Create(post, kind, number, now);

        var toDelete = SelectForDeletion(existing, kind, clearAutosaves, options.AutosaveKeep, options.MaxRevisions);
        if (toDelete.Count > 0)
            await posts.DeleteRevisionsAsync(toDelete, cancellationToken);

        revisions.Add(revision);
        return revision;
    }

    /// <summary>Yangi reviziya qo'shilganda o'chirilishi kerak bo'lgan mavjud reviziyalar.</summary>
    internal static IReadOnlyList<Guid> SelectForDeletion(IReadOnlyList<RevisionHeader> existing, RevisionKind newKind,
        bool clearAutosaves, int autosaveKeep, int maxRevisions)
    {
        var remaining = existing.OrderBy(r => r.Number).ToList();
        var deleted = new List<Guid>();

        var autosaves = remaining.Where(r => r.Kind == RevisionKind.Autosave).ToList();
        var keepAutosaves = clearAutosaves ? 0
            : newKind == RevisionKind.Autosave ? Math.Max(autosaveKeep, 1) - 1
            : autosaveKeep;

        foreach (var old in autosaves.Take(Math.Max(autosaves.Count - keepAutosaves, 0)))
        {
            deleted.Add(old.Id);
            remaining.Remove(old);
        }

        // Yangisi bilan birga limitdan oshmasin.
        var overflow = remaining.Count + 1 - Math.Max(maxRevisions, 1);
        foreach (var old in remaining.Take(Math.Max(overflow, 0)))
            deleted.Add(old.Id);

        return deleted;
    }
}
