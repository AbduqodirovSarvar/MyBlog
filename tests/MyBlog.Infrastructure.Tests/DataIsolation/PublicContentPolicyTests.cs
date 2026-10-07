using Microsoft.Extensions.Options;
using MyBlog.Application.Abstractions.Authorization;
using MyBlog.Infrastructure.DataIsolation;
using NSubstitute;

namespace MyBlog.Infrastructure.Tests.DataIsolation;

public sealed class PublicContentPolicyTests
{
    private static readonly Guid Alice = Guid.CreateVersion7();
    private static readonly Guid Bob = Guid.CreateVersion7();

    private static IPublicContentPolicy Create(bool publicRead, Guid? userId, bool isolationEnabled = true, bool bypass = false)
    {
        var isolation = Substitute.For<IDataIsolationContext>();
        isolation.IsEnabled.Returns(isolationEnabled);
        isolation.Bypass.Returns(bypass);
        isolation.CurrentUserId.Returns(userId);

        return new PublicContentPolicy(isolation, Options.Create(new DataIsolationOptions { PublicReadOfPublishedContent = publicRead }));
    }

    [Fact]
    public void Default_option_is_public_read()
    {
        new DataIsolationOptions().PublicReadOfPublishedContent.ShouldBeTrue();
    }

    [Fact]
    public void Public_read_enabled_has_no_restriction()
    {
        var policy = Create(publicRead: true, userId: null);

        policy.IsPublicReadEnabled.ShouldBeTrue();
        policy.OwnerScope.ShouldBeNull();
        policy.CanReadOthersContent.ShouldBeTrue();
        policy.CanRead(Bob).ShouldBeTrue();
    }

    [Fact]
    public void Closed_system_restricts_user_to_own_content()
    {
        var policy = Create(publicRead: false, userId: Alice);

        policy.IsPublicReadEnabled.ShouldBeFalse();
        policy.OwnerScope.ShouldBe(Alice);
        policy.CanReadOthersContent.ShouldBeFalse();
        policy.CanRead(Alice).ShouldBeTrue();
        policy.CanRead(Bob).ShouldBeFalse();
    }

    [Fact]
    public void Closed_system_anonymous_sees_nothing()
    {
        var policy = Create(publicRead: false, userId: null);

        policy.OwnerScope.ShouldBe(Guid.Empty);
        policy.CanRead(Bob).ShouldBeFalse();
        policy.CanRead(Guid.Empty).ShouldBeFalse();
    }

    [Theory]
    [InlineData(false, false)] // isolation o'chiq — ownership filtri ham yo'q
    [InlineData(true, true)] // bypass rol (SuperAdmin)
    public void Closed_system_is_not_applied_when_isolation_is_off_or_bypassed(bool bypass, bool isolationEnabled)
    {
        var policy = Create(publicRead: false, userId: Alice, isolationEnabled, bypass);

        policy.OwnerScope.ShouldBeNull();
        policy.CanRead(Bob).ShouldBeTrue();
    }
}
