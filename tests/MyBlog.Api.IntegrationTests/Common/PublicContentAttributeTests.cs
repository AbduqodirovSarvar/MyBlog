using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using MyBlog.Api.Common;
using MyBlog.Api.Controllers;
using MyBlog.Application.Abstractions.Authorization;

namespace MyBlog.Api.IntegrationTests.Common;

/// <summary>[PublicContent] filtri (bazasiz): yopiq tizimda so'rov 404 bilan to'xtaydi, ochiqda o'tkaziladi.</summary>
public sealed class PublicContentAttributeTests
{
    private sealed class Policy(Guid? ownerScope) : IPublicContentPolicy
    {
        public bool IsPublicReadEnabled => OwnerScope is null;
        public Guid? OwnerScope { get; } = ownerScope;
    }

    private static ResourceExecutingContext Execute(IPublicContentPolicy policy)
    {
        var services = new ServiceCollection().AddScoped(_ => policy).BuildServiceProvider();
        var httpContext = new DefaultHttpContext { RequestServices = services.CreateScope().ServiceProvider };
        var context = new ResourceExecutingContext(
            new ActionContext(httpContext, new RouteData(), new ActionDescriptor()), [], []);

        var filter = (IResourceFilter)new PublicContentAttribute().CreateInstance(services);
        filter.OnResourceExecuting(context);
        return context;
    }

    [Fact]
    public void Open_system_lets_request_through()
    {
        Execute(new Policy(null)).Result.ShouldBeNull();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Closed_system_short_circuits_with_general_not_found(bool authenticated)
    {
        var context = Execute(new Policy(authenticated ? Guid.CreateVersion7() : Guid.Empty));

        context.Result.ShouldBeOfType<ErrorActionResult>().Error.Code.ShouldBe("General.NotFound");
    }

    [Theory]
    [InlineData(typeof(PublicPostsController))]
    [InlineData(typeof(AuthorsController))]
    public void Public_controllers_are_marked(Type controller)
    {
        controller.GetCustomAttributes(typeof(PublicContentAttribute), inherit: true).ShouldNotBeEmpty();
    }
}
