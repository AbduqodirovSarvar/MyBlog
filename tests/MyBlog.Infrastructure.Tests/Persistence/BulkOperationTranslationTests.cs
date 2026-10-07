using MyBlog.Infrastructure.DataIsolation;
using MyBlog.Infrastructure.Persistence;
using MyBlog.Infrastructure.Persistence.Repositories;
using Npgsql;

namespace MyBlog.Infrastructure.Tests.Persistence;

/// <summary>
/// ExecuteDelete/ExecuteUpdate owned type'li (table splitting, JSON) entity'larda tarjima bo'lishini tekshiradi:
/// so'rov yopiq portga yuboriladi — tarjima muvaffaqiyatli bo'lsa xato faqat ulanishdan (NpgsqlException,
/// retry strategiyasi o'rab berishi mumkin) keladi.
/// </summary>
public sealed class BulkOperationTranslationTests
{
    private static AppDbContext CreateContext() =>
        new(AppDbContextFactory.CreateDesignTimeOptions("Host=127.0.0.1;Port=1;Timeout=1;Database=none"),
            DisabledDataIsolationContext.Instance);

    private static async Task ShouldFailOnlyOnConnectAsync(Func<Task> operation)
    {
        var exception = await Should.ThrowAsync<Exception>(operation);
        var connectionFailure = exception as NpgsqlException ?? exception.InnerException as NpgsqlException;
        connectionFailure.ShouldNotBeNull(exception.ToString());
    }

    [Fact]
    public async Task Media_delete_if_unreferenced_translates()
    {
        await using var context = CreateContext();

        await ShouldFailOnlyOnConnectAsync(() =>
            new MediaRepository(context).DeleteIfUnreferencedAsync(Guid.NewGuid(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Revision_delete_and_view_count_update_translate()
    {
        await using var context = CreateContext();
        var repository = new PostRepository(context);

        await ShouldFailOnlyOnConnectAsync(() =>
            repository.DeleteRevisionsAsync([Guid.NewGuid()], TestContext.Current.CancellationToken));
        await ShouldFailOnlyOnConnectAsync(() =>
            repository.IncrementViewCountAsync(Guid.NewGuid(), TestContext.Current.CancellationToken));
    }
}
