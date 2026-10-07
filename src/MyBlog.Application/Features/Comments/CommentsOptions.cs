using System.ComponentModel.DataAnnotations;
using MyBlog.Domain.Comments;

namespace MyBlog.Application.Features.Comments;

/// <summary>appsettings "Comments" bo'limi. Qiymatlar domain cheklovlaridan oshmasligi kerak.</summary>
public sealed class CommentsOptions
{
    public const string SectionName = "Comments";

    /// <summary>Daraxt darajalari soni (1 — faqat ildiz izohlar, 3 — Depth 0..2).</summary>
    [Range(1, CommentConstraints.MaxDepth)]
    public int MaxDepth { get; set; } = CommentConstraints.MaxDepth;

    [Range(1, CommentConstraints.ContentMaxLength)]
    public int MaxLength { get; set; } = CommentConstraints.ContentMaxLength;

    /// <summary>Muallif izohni yaratilgandan keyin necha daqiqa ichida tahrirlay oladi (0 — cheklovsiz).</summary>
    [Range(0, int.MaxValue)]
    public int EditWindowMinutes { get; set; }
}

/// <summary>Bildirishnoma xatlaridagi havolalar uchun ("Frontend" bo'limidan faqat BaseUrl o'qiladi).</summary>
public sealed class CommentLinkOptions
{
    public const string SectionName = "Frontend";

    public string BaseUrl { get; set; } = "http://localhost:4200";
}
