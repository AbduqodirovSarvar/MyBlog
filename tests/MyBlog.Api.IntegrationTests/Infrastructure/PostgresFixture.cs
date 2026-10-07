using Npgsql;
using Testcontainers.PostgreSql;

namespace MyBlog.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Testcontainers orqali PostgreSQL. Docker ishlamayotgan bo'lsa exception tashlanmaydi —
/// testlar <see cref="SkipIfUnavailable"/> orqali skip qilinadi.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    public string? ConnectionString { get; private set; }

    public string? UnavailableReason { get; private set; }

    public async ValueTask InitializeAsync()
    {
        try
        {
            _container = new PostgreSqlBuilder("postgres:17-alpine").Build();
            await _container.StartAsync();
            ConnectionString = _container.GetConnectionString();
        }
        catch (Exception ex)
        {
            UnavailableReason = $"Docker/PostgreSQL is not available ({ex.GetType().Name}: {ex.Message})";
            if (_container is not null)
            {
                await _container.DisposeAsync();
                _container = null;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }

    public void SkipIfUnavailable() => Assert.SkipWhen(ConnectionString is null, UnavailableReason ?? "PostgreSQL is not available");

    /// <summary>Alohida test bazasi uchun connection string (baza EnsureCreated bilan yaratiladi).</summary>
    public string ConnectionStringFor(string database)
    {
        SkipIfUnavailable();
        return new NpgsqlConnectionStringBuilder(ConnectionString) { Database = database }.ConnectionString;
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Postgres";
}
