using API.Application.Bundles;
using API.Application.Services.Abstractions;
using API.Application.Services.Features.Call;
using API.Domain.Common;
using API.Web.Hubs;
using System.Text.Json;
using System.Text.Json.Serialization;
using API.Application;
using API.Infrastructure;

namespace API.Web.Configuration;

public static class DependencyInjection
{
    public static IServiceCollection AddMessengerDatabase(this IServiceCollection services,IConfiguration configuration,IWebHostEnvironment environment)
    {
        // Делегируем в Infrastructure
        services.AddInfrastructure(configuration, environment);
        return services;
    }

    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<AppDateTime>();

        // Web-специфика: HubNotifier регистрируется здесь,
        // потому что зависит от IHubContext<MessengerHub>
        services.AddScoped<IHubNotifier, HubNotifier>();

        services.AddBundles();

        services.Configure<TurnSettings>(
            configuration.GetSection(TurnSettings.Section));
        services.AddSingleton<TurnCredentialService>();

        return services;
    }

    public static IServiceCollection AddBusinessServices(
        this IServiceCollection services)
    {
        // Делегируем в Application
        services.AddApplication();
        return services;
    }

    private static IServiceCollection AddBundles(this IServiceCollection services)
    {
        services.AddScoped<TimeBundle>();
        services.AddScoped<UrlBundle>();
        services.AddScoped<CacheBundle>();
        services.AddScoped<NotificationBundle>();
        services.AddScoped<MediaBundle>();
        services.AddScoped<PresenceBundle>();
        services.AddScoped<ChatBundle>();

        return services;
    }

    public static IServiceCollection AddMessengerJson(
        this IServiceCollection services,
        IWebHostEnvironment environment)
    {
        void configureJson(JsonSerializerOptions options)
        {
            options.ReferenceHandler = ReferenceHandler.IgnoreCycles;
            options.WriteIndented = environment.IsDevelopment();
        }

        services.ConfigureHttpJsonOptions(
            options => configureJson(options.SerializerOptions));
        services.AddControllers()
            .AddJsonOptions(
                options => configureJson(options.JsonSerializerOptions));

        return services;
    }
}