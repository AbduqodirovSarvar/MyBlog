using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MyBlog.Application;
using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Application.Features.Authors.Abstractions;
using MyBlog.Application.Features.Authors.Common;
using MyBlog.Application.Features.Authors.GetAuthorProfile;
using MyBlog.Application.Features.Categories.Common;
using MyBlog.Application.Features.Categories.CreateCategory;
using MyBlog.Application.Features.Profile.Common;
using MyBlog.Application.Features.Profile.Skills;
using MyBlog.Application.Features.Tags.Abstractions;
using MyBlog.Domain.Common;
using MyBlog.Infrastructure.Persistence.Repositories;
using NSubstitute;

namespace MyBlog.Infrastructure.Tests.Modules;

public sealed class ProfileModuleTests
{
    [Fact]
    public async Task Profile_module_services_and_handlers_are_resolvable()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "Host=localhost;Database=di_only",
                ["Smtp:Host"] = "localhost",
                ["Smtp:FromAddress"] = "no-reply@myblog.local"
            })
            .Build();
        var environment = Substitute.For<IHostEnvironment>();
        environment.ContentRootPath.Returns(Path.GetTempPath());
        environment.EnvironmentName.Returns(Environments.Development);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(environment);
        services.AddScoped(_ => Substitute.For<ICurrentUser>());
        services.AddApplication();
        services.AddInfrastructure(configuration, environment);

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        sp.GetRequiredService<ITagResolver>().ShouldNotBeNull();
        sp.GetRequiredService<IContentStatsRepository>().ShouldBeOfType<ContentStatsRepository>();
        sp.GetRequiredService<IRequestHandler<CreateCategoryCommand, Result<CategoryDto>>>().ShouldNotBeNull();
        sp.GetRequiredService<IRequestHandler<AddSkillCommand, Result<SkillDto>>>().ShouldNotBeNull();
        sp.GetRequiredService<IRequestHandler<GetAuthorProfileQuery, Result<AuthorProfileDto>>>().ShouldNotBeNull();
    }
}
