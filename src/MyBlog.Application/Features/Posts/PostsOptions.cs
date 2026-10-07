using System.ComponentModel.DataAnnotations;

namespace MyBlog.Application.Features.Posts;

/// <summary>"Posts" bo'limi. Infrastructure (PostsModule) bog'laydi.</summary>
public sealed class PostsOptions
{
    public const string SectionName = "Posts";

    /// <summary>Post bo'yicha saqlanadigan Autosave reviziyalari soni (eng yangilari).</summary>
    [Range(1, 20)]
    public int AutosaveKeep { get; set; } = 1;

    /// <summary>Post bo'yicha jami reviziyalar limiti; oshganlari (eng eskilari) o'chiriladi.</summary>
    [Range(1, 1000)]
    public int MaxRevisions { get; set; } = 50;

    /// <summary>Public post sahifasi kesh muddati (soniya).</summary>
    [Range(1, 86_400)]
    public int PublicDetailCacheSeconds { get; set; } = 120;

    /// <summary>Rejalashtirilgan postlarni nashr qiluvchi job bir ishga tushishda nechta postni oladi.</summary>
    [Range(1, 10_000)]
    public int ScheduledPublishBatchSize { get; set; } = 100;
}
