using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Application.Common.Behaviors;

namespace MyBlog.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Handler'lar, domain event handler'lar, validator'lar va recurring job'lar assembly'dan
    /// avtomatik topiladi. Yangi modul qo'shganda bu faylni o'zgartirish shart emas.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.TryAddScoped<ISender, Sender>();
        services.TryAddScoped<IDomainEventDispatcher, DomainEventDispatcher>();

        // Tartib muhim: Logging eng tashqi, Validation undan keyin.
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        RegisterClosedImplementations(services, assembly, typeof(IRequestHandler<,>));
        RegisterClosedImplementations(services, assembly, typeof(IDomainEventHandler<>));

        foreach (var jobType in ConcreteTypes(assembly).Where(t => typeof(IRecurringJob).IsAssignableFrom(t)))
            services.AddScoped(typeof(IRecurringJob), jobType);

        return services;
    }

    private static void RegisterClosedImplementations(IServiceCollection services, Assembly assembly, Type openInterface)
    {
        foreach (var type in ConcreteTypes(assembly))
        {
            var interfaces = type.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == openInterface);

            foreach (var @interface in interfaces)
                services.AddScoped(@interface, type);
        }
    }

    private static IEnumerable<Type> ConcreteTypes(Assembly assembly) =>
        assembly.GetTypes().Where(t => t is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false });
}
