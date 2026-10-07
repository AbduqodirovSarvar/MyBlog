using System.Net;
using MyBlog.Api.IntegrationTests.Infrastructure;

namespace MyBlog.Api.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class HealthCheckTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Health_endpoint_reports_healthy_database()
    {
        postgres.SkipIfUnavailable();
        await using var factory = new ApiFactory(postgres.ConnectionString!);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBe("Healthy");
    }

    [Theory]
    [InlineData("ru", "ru")]
    [InlineData("uz-Cyrl", "uz-Cyrl")]
    [InlineData("de", "uz")]
    public async Task Culture_is_resolved_from_query_string(string requested, string expected)
    {
        postgres.SkipIfUnavailable();
        await using var factory = new ApiFactory(postgres.ConnectionString!);
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/health?culture={requested}", TestContext.Current.CancellationToken);

        response.Content.Headers.ContentLanguage.ShouldContain(expected);
    }
}
