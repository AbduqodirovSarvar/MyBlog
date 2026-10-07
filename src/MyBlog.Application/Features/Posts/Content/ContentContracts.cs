using MyBlog.Domain.Common;

namespace MyBlog.Application.Features.Posts.Content;

/// <param name="Format">Muharrir formati: "html", "tiptap-json", "ckeditor5" ...</param>
/// <param name="Body">Muharrir chiqargan HTML (har doim majburiy — render shundan qilinadi).</param>
/// <param name="Raw">Muharrirning o'z hujjati (masalan TipTap JSON) — o'zgarmasdan saqlanadi, hech qachon render qilinmaydi.</param>
public sealed record ContentInput(string Format, string Body, string? Raw = null);

/// <summary>Sarlavha (h2/h3) — mundarija elementi.</summary>
public sealed record TocItem(int Level, string Id, string Text);

/// <param name="Html">Sanitize qilingan, media URL'lari qayta yozilgan, sarlavhalarga id qo'yilgan HTML.</param>
/// <param name="MediaIds">Kontentdagi data-media-id'lar (muallifga tegishliligi tekshirilgan).</param>
public sealed record ProcessedContent(
    string Format,
    string Html,
    string? Raw,
    string PlainText,
    int ReadingTimeMinutes,
    IReadOnlyList<Guid> MediaIds,
    IReadOnlyList<TocItem> Toc);

/// <summary>Bitta yoki bir nechta muharrir formatini qayta ishlovchi strategiya.</summary>
public interface IContentProcessor
{
    IReadOnlyCollection<string> Formats { get; }

    Task<Result<ProcessedContent>> ProcessAsync(ContentInput input, Guid ownerId, CancellationToken cancellationToken = default);
}

public interface IContentProcessorFactory
{
    /// <summary>Format bo'yicha strategiya; noma'lum format — Post.UnsupportedContentFormat.</summary>
    Result<IContentProcessor> Get(string format);
}
