using API.Application.Common;
using API.Application.Features.Department;
using API.Application.Services.Abstractions;
using API.Application.Services.Features.Chat;
using API.Application.Services.Features.ReadReceipt;
using API.Application.Services.Features.Status;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace API.Application;

public static class ApplicationExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services
            .AddHandlers(assembly)
            .AddHandlerAggregates(assembly)
            .AddBundles(assembly)
            .AddApplicationServices();

        return services;
    }

    private static IServiceCollection AddHandlers(this IServiceCollection services, Assembly assembly)
    {
        var handlerTypes = assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(t => t.GetInterfaces().Any(i =>
                i.IsGenericType &&
                (i.GetGenericTypeDefinition() == typeof(ICommandHandler<>) ||
                 i.GetGenericTypeDefinition() == typeof(ICommandHandler<,>) ||
                 i.GetGenericTypeDefinition() == typeof(IQueryHandler<,>))));

        foreach (var type in handlerTypes)
            services.AddScoped(type);

        return services;
    }

    private static IServiceCollection AddHandlerAggregates(this IServiceCollection services, Assembly assembly)
    {
        var aggregateTypes = assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(t => t.Name.EndsWith("Handlers"));

        foreach (var type in aggregateTypes)
        {
            var iface = type.GetInterfaces().FirstOrDefault(i => i.Name == "I" + type.Name);
            if (iface != null)
                services.AddScoped(iface, type);
            else
                services.AddScoped(type);
        }

        return services;
    }

    private static IServiceCollection AddBundles(this IServiceCollection services, Assembly assembly)
    {
        var bundleTypes = assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(t => t.Name.EndsWith("Bundle"));

        foreach (var type in bundleTypes)
            services.AddScoped(type);

        return services;
    }

    private static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<IChatMemberService, ChatMemberService>();
        services.AddScoped<ISystemMessageService, SystemMessageService>();
        services.AddScoped<IReadReceiptService, ReadReceiptService>();
        services.AddScoped<IUserStatusService, UserStatusService>();
        services.AddScoped<DepartmentSyncService>();
        return services;
    }
}
