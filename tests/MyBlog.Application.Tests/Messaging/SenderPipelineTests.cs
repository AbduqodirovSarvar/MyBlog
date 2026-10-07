using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Application.Common.Behaviors;
using MyBlog.Domain.Common;
using NSubstitute;

namespace MyBlog.Application.Tests.Messaging;

public sealed record CreateThing(string Name) : ICommand<string>;

internal sealed class CallLog
{
    public List<string> Entries { get; } = [];
}

internal sealed class CreateThingHandler(CallLog log) : ICommandHandler<CreateThing, string>
{
    public Task<Result<string>> Handle(CreateThing request, CancellationToken cancellationToken)
    {
        log.Entries.Add("handler");
        return Task.FromResult(Result.Success($"created:{request.Name}"));
    }
}

internal sealed class CreateThingValidator : AbstractValidator<CreateThing>
{
    public CreateThingValidator() =>
        RuleFor(x => x.Name).NotEmpty().WithErrorCode("Things.NameRequired").WithMessage("Name is required.");
}

internal sealed class OuterBehavior<TRequest, TResponse>(CallLog log) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        log.Entries.Add("outer:before");
        var response = await next();
        log.Entries.Add("outer:after");
        return response;
    }
}

internal sealed class InnerBehavior<TRequest, TResponse>(CallLog log) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        log.Entries.Add("inner:before");
        var response = await next();
        log.Entries.Add("inner:after");
        return response;
    }
}

public sealed class SenderPipelineTests
{
    private static ServiceProvider BuildProvider(Action<IServiceCollection> configure, ILocalizer? localizer = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<CallLog>();
        services.AddScoped<ISender, Sender>();
        services.AddScoped<IRequestHandler<CreateThing, Result<string>>, CreateThingHandler>();
        services.AddSingleton(localizer ?? NotTranslatingLocalizer());
        configure(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    // NSubstitute string uchun "" qaytaradi; tarjima yo'qligini null bilan ifodalaymiz.
    private static ILocalizer NotTranslatingLocalizer()
    {
        var localizer = Substitute.For<ILocalizer>();
        localizer.Find(Arg.Any<string>(), Arg.Any<string?>()).Returns((string?)null);
        return localizer;
    }

    [Fact]
    public async Task Behaviors_wrap_handler_in_registration_order()
    {
        await using var provider = BuildProvider(services =>
        {
            services.AddScoped(typeof(IPipelineBehavior<,>), typeof(OuterBehavior<,>));
            services.AddScoped(typeof(IPipelineBehavior<,>), typeof(InnerBehavior<,>));
        });
        await using var scope = provider.CreateAsyncScope();

        var result = await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new CreateThing("post"), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe("created:post");
        provider.GetRequiredService<CallLog>().Entries.ShouldBe(
            ["outer:before", "inner:before", "handler", "inner:after", "outer:after"]);
    }

    [Fact]
    public async Task Validation_failure_short_circuits_and_returns_localized_validation_error()
    {
        var localizer = NotTranslatingLocalizer();
        localizer.Find("Things.NameRequired").Returns("Nom kiritilishi shart.");

        await using var provider = BuildProvider(services =>
        {
            services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
            services.AddScoped<IValidator<CreateThing>, CreateThingValidator>();
        }, localizer);
        await using var scope = provider.CreateAsyncScope();

        var result = await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new CreateThing(""), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        var error = result.Error.ShouldBeOfType<ValidationError>();
        error.Type.ShouldBe(ErrorType.Validation);
        error.Code.ShouldBe("General.Validation");
        error.Errors.ShouldHaveSingleItem().ShouldBe(new FieldError("name", "Things.NameRequired", "Nom kiritilishi shart."));
        provider.GetRequiredService<CallLog>().Entries.ShouldNotContain("handler");
    }

    [Fact]
    public async Task Validation_uses_validator_message_when_code_is_not_localized()
    {
        await using var provider = BuildProvider(services =>
        {
            services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
            services.AddScoped<IValidator<CreateThing>, CreateThingValidator>();
        });
        await using var scope = provider.CreateAsyncScope();

        var result = await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new CreateThing(" "), TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType<ValidationError>().Errors.Single().Message.ShouldBe("Name is required.");
    }

    [Fact]
    public async Task Valid_request_reaches_handler()
    {
        await using var provider = BuildProvider(services =>
        {
            services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
            services.AddScoped<IValidator<CreateThing>, CreateThingValidator>();
        });
        await using var scope = provider.CreateAsyncScope();

        var result = await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new CreateThing("ok"), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        provider.GetRequiredService<CallLog>().Entries.ShouldBe(["handler"]);
    }

    [Fact]
    public void AddApplication_registers_sender_and_behaviors_in_order()
    {
        var services = new ServiceCollection();
        services.AddApplication();

        services.ShouldContain(d => d.ServiceType == typeof(ISender));
        var behaviors = services.Where(d => d.ServiceType == typeof(IPipelineBehavior<,>)).Select(d => d.ImplementationType).ToList();
        behaviors.ShouldBe([typeof(LoggingBehavior<,>), typeof(ValidationBehavior<,>)]);
    }
}
