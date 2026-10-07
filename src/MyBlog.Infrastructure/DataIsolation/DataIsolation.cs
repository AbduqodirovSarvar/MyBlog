using Microsoft.Extensions.Options;
using MyBlog.Application.Abstractions.Authorization;
using MyBlog.Application.Abstractions.Services;

namespace MyBlog.Infrastructure.DataIsolation;

internal sealed class DataIsolationOptions
{
    public const string SectionName = "DataIsolation";

    /// <summary>Ownership query filter yoqilganmi.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Bu rollardagi foydalanuvchilar barcha egalarning ma'lumotini ko'radi.</summary>
    public string[] BypassRoles { get; set; } = [Roles.SuperAdmin];

    /// <summary>Nashr qilingan kontentni anonim/boshqa foydalanuvchilar o'qiy oladimi (modul query'lari uchun).</summary>
    public bool PublicReadOfPublishedContent { get; set; } = true;
}

/// <summary>
/// Ownership filtri uchun joriy holat. DbContext uni har bir so'rovda o'qiydi (EF parametr qiladi).
/// </summary>
internal interface IDataIsolationContext
{
    bool IsEnabled { get; }

    /// <summary>Filtrni chetlab o'tish (masalan SuperAdmin).</summary>
    bool Bypass { get; }

    /// <summary>Joriy foydalanuvchi; anonim bo'lsa null.</summary>
    Guid? CurrentUserId { get; }
}

internal sealed class DataIsolationContext(ICurrentUser currentUser, IOptions<DataIsolationOptions> options)
    : IDataIsolationContext
{
    public bool IsEnabled => options.Value.Enabled;

    public bool Bypass => options.Value.BypassRoles.Any(currentUser.IsInRole);

    public Guid? CurrentUserId => currentUser.Id;
}

/// <summary>Design-time (migratsiya) va maxsus holatlar uchun: filtr o'chiq.</summary>
internal sealed class DisabledDataIsolationContext : IDataIsolationContext
{
    public static readonly DisabledDataIsolationContext Instance = new();

    public bool IsEnabled => false;
    public bool Bypass => true;
    public Guid? CurrentUserId => null;
}
