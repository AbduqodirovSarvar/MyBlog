using System.Text;
using MyBlog.Domain.Comments;
using MyBlog.Domain.Common;

namespace MyBlog.Application.Features.Comments;

/// <summary>
/// Izoh matni — faqat plain text. HTML'ga aylantirilmaydi (frontend matn sifatida ko'rsatadi):
/// qator oxirlari "\n" ga keltiriladi, boshqaruv belgilari olib tashlanadi, 2 tadan ortiq bo'sh qator qisqartiriladi.
/// </summary>
internal static class CommentContent
{
    private const int MaxConsecutiveNewLines = 2;

    public static string Normalize(string? content)
    {
        if (string.IsNullOrEmpty(content))
            return string.Empty;

        var builder = new StringBuilder(content.Length);
        var newLines = 0;

        for (var i = 0; i < content.Length; i++)
        {
            var ch = content[i];

            if (ch == '\r')
            {
                if (i + 1 < content.Length && content[i + 1] == '\n')
                    continue;
                ch = '\n';
            }

            if (ch == '\n')
            {
                if (++newLines <= MaxConsecutiveNewLines)
                    builder.Append(ch);
                continue;
            }

            // Tab'dan boshqa boshqaruv belgilari (va bidi override'lar) tashlanadi.
            if (ch != '\t' && (char.IsControl(ch) || IsBidiControl(ch)))
                continue;

            if (!char.IsWhiteSpace(ch))
                newLines = 0;

            builder.Append(ch);
        }

        return builder.ToString().Trim();
    }

    /// <summary>Normalize + uzunlik tekshiruvi (sozlamadagi chegaraga ko'ra).</summary>
    public static Result<string> Validate(string? content, int maxLength)
    {
        var value = Normalize(content);
        if (value.Length == 0)
            return CommentErrors.ContentRequired;
        if (value.Length > maxLength)
            return CommentErrors.ContentTooLong.WithArgs(maxLength);

        return value;
    }

    private static bool IsBidiControl(char ch) => ch is >= '‪' and <= '‮' or >= '⁦' and <= '⁩';
}
