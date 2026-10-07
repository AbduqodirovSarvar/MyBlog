namespace MyBlog.Application.Features.Media.Abstractions;

/// <summary>Fayl turini "magic bytes" bo'yicha aniqlaydi (kengaytma yoki Content-Type'ga ishonilmaydi).</summary>
public interface IFileSignatureValidator
{
    /// <summary>Aniqlash uchun kerak bo'ladigan bosh qism uzunligi (bayt).</summary>
    int HeaderLength { get; }

    /// <summary>Aniqlangan kanonik content type (masalan "image/jpeg"); noma'lum bo'lsa null.</summary>
    string? DetectContentType(ReadOnlySpan<byte> header);

    /// <summary>Fayl mazmuni e'lon qilingan content type'ga mosmi ("image/jpg" kabi sinonimlar hisobga olinadi).</summary>
    bool Matches(ReadOnlySpan<byte> header, string declaredContentType);

    /// <summary>"image/jpg", "image/pjpeg" → "image/jpeg" va h.k.; kichik harflarga keltiradi.</summary>
    string NormalizeContentType(string contentType);
}
