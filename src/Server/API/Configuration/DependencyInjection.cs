using API.Application;
using API.Application.Configuration;
using API.Application.Services.Abstractions;
using API.Domain.Common;
using API.Infrastructure;
using API.Infrastructure.Services.Call;
using API.Infrastructure.Services.Features.Call;
using API.Web.Hubs;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace API.Web.Configuration;

public static class DependencyInjection
{
    public static IServiceCollection AddMessengerDatabase(this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
    {
        services.AddInfrastructure(configuration, environment);
        return services;
    }

    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<AppDateTime>();
        services.AddScoped<IHubNotifier, HubNotifier>();

        services.Configure<TurnSettings>(configuration.GetSection(TurnSettings.Section));
        services.AddSingleton<TurnCredentialService>();

        return services;
    }

    public static IServiceCollection AddBusinessServices(this IServiceCollection services)
    {
        services.AddApplication();
        return services;
    }

    public static IServiceCollection AddMessengerJson(this IServiceCollection services, IWebHostEnvironment environment)
    {
        void ConfigureJson(JsonSerializerOptions options)
        {
            options.ReferenceHandler = ReferenceHandler.IgnoreCycles;
            options.WriteIndented = environment.IsDevelopment();
        }

        services.ConfigureHttpJsonOptions(options => ConfigureJson(options.SerializerOptions));

        services.AddControllers().AddJsonOptions(options => ConfigureJson(options.JsonSerializerOptions));

        return services;
    }
}