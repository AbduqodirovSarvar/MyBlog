using System.ComponentModel.DataAnnotations;

namespace MyBlog.Infrastructure.Email;

internal sealed class EmailOutboxOptions
{
    public const string SectionName = "EmailOutbox";

    /// <summary>Yangi xat signali bo'lmasa ham jadval shu oraliqda tekshiriladi.</summary>
    [Range(1, 3600)]
    public int PollIntervalSeconds { get; set; } = 10;

    [Range(1, 1000)]
    public int BatchSize { get; set; } = 20;

    /// <summary>Shuncha urinishdan keyin xat Failed holatiga o'tadi.</summary>
    [Range(1, 100)]
    public int MaxAttempts { get; set; } = 8;

    /// <summary>Processing holatidagi qator qulfi; muddati o'tsa (instance qulagan) qayta navbatga qaytariladi.</summary>
    [Range(10, 86400)]
    public int LockSeconds { get; set; } = 120;

    /// <summary>Yuborilgan (Sent) qatorlar shuncha kundan keyin o'chiriladi.</summary>
    [Range(1, 3650)]
    public int RetentionDays { get; set; } = 14;
}

/// <summary>Qayta urinish jadvali: 1m, 5m, 30m, 2h, 12h, keyin har 12h.</summary>
internal static class EmailOutboxBackoff
{
    private static readonly TimeSpan[] Schedule =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(2),
        TimeSpan.FromHours(12)
    ];

    /// <param name="attempts">Shu paytgacha bajarilgan (muvaffaqiyatsiz) urinishlar soni, 1 dan boshlab.</param>
    public static TimeSpan GetDelay(int attempts) => Schedule[Math.Clamp(attempts, 1, Schedule.Length) - 1];
}
