using Microsoft.EntityFrameworkCore;
using MyBlog.Infrastructure.DataIsolation;
using MyBlog.Infrastructure.Identity;
using MyBlog.Infrastructure.Persistence;

namespace MyBlog.Infrastructure.Tests.Persistence;

/// <summary>Model bazaga ulanmasdan quriladi (design-time factory bilan bir xil yo'l).</summary>
public sealed class AppDbContextModelTests
{
    private static AppDbContext CreateContext() =>
        new(AppDbContextFactory.CreateDesignTimeOptions("Host=localhost;Database=model_only"), DisabledDataIsolationContext.Instance);

    [Fact]
    public void Model_builds_without_application_services()
    {
        using var context = CreateContext();

        Should.NotThrow(() => context.Model);
    }

    [Theory]
    [InlineData(typeof(ApplicationUser), "users")]
    [InlineData(typeof(ApplicationRole), "roles")]
    [InlineData(typeof(RolePermission), "role_permissions")]
    [InlineData(typeof(RefreshToken), "refresh_tokens")]
    [InlineData(typeof(Microsoft.AspNetCore.Identity.IdentityUserPasskey<Guid>), "user_passkeys")]
    [InlineData(typeof(Microsoft.AspNetCore.Identity.IdentityUserRole<Guid>), "user_roles")]
    public void Identity_tables_use_snake_case_names(Type entityType, string table)
    {
        using var context = CreateContext();

        context.Model.FindEntityType(entityType)!.GetTableName().ShouldBe(table);
    }

    [Fact]
    public void Refresh_token_hash_has_unique_index()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(RefreshToken))!;

        entity.GetIndexes().ShouldContain(i => i.IsUnique && i.Properties.Single().Name == nameof(RefreshToken.TokenHash));
        entity.GetIndexes().ShouldContain(i => i.Properties.Single().Name == nameof(RefreshToken.UserId));
    }
}
