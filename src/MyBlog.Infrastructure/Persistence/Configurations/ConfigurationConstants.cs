namespace MyBlog.Infrastructure.Persistence.Configurations;

internal static class ConfigurationConstants
{
    /// <summary>String sifatida saqlanadigan enum ustunlarining uzunligi.</summary>
    public const int EnumMaxLength = 32;

    /// <summary>Soft delete qilinmagan yozuvlar uchun partial unique index filtri.</summary>
    public const string NotDeletedFilter = "\"is_deleted\" = false";

    /// <summary>PostgreSQL xmin ustuniga map qilinadigan optimistic concurrency shadow property nomi.</summary>
    public const string VersionProperty = "Version";
}
