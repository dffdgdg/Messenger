using MessengerAPI.Model;
using MessengerAPI.Services;
using MessengerAPI.Services.Auth;
using MessengerAPI.Services.Chat;
using MessengerAPI.Services.Department;
using MessengerAPI.Services.Infrastructure;
using MessengerAPI.Services.Messaging;
using MessengerAPI.Services.User;
using Microsoft.EntityFrameworkCore;

namespace MessengerAPI.Configuration;

public static class DependencyInjection
{
    /// <summary>
    /// Регистрация DbContext
    /// </summary>
    public static IServiceCollection AddMessengerDatabase(this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
    {
        services.AddDbContext<MessengerDbContext>(options =>
        {
            options.UseNpgsql(configuration.GetConnectionString("DefaultConnection"), npgsql =>
                {
                    npgsql.MapEnum<MessengerShared.Enum.Theme>("theme");
                    npgsql.MapEnum<MessengerShared.Enum.ChatRole>("chat_role");
                    npgsql.MapEnum<MessengerShared.Enum.ChatType>("chat_type");
                });

            if (environment.IsDevelopment())
            {
                options.EnableSensitiveDataLogging();
                options.EnableDetailedErrors();
            }
        });

        return services;
    }

    /// <summary>
    /// Инфраструктурные сервисы (кэш, онлайн, доступ, файлы, токены)
    /// </summary>
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddHttpContextAccessor();

        services.AddSingleton<IOnlineUserService, OnlineUserService>();
        services.AddScoped<ICacheService, CacheService>();
        services.AddScoped<IAccessControlService, AccessControlService>();
        services.AddScoped<IFileService, FileService>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IHubNotifier, HubNotifier>();
        services.AddScoped<IUrlBuilder, HttpUrlBuilder>();

        return services;
    }

    /// <summary>
    /// Бизнес-сервисы
    /// </summary>
    public static IServiceCollection AddBusinessServices(this IServiceCollection services)
    {
        // Auth
        services.AddScoped<IAuthService, AuthService>();

        // Users
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IAdminService, AdminService>();

        // Chats
        services.AddScoped<IChatService, ChatService>();
        services.AddScoped<IChatMemberService, ChatMemberService>();
        services.AddScoped<INotificationService, NotificationService>();

        // Messaging
        services.AddScoped<IMessageService, MessageService>();
        services.AddScoped<IPollService, PollService>();
        services.AddScoped<IReadReceiptService, ReadReceiptService>();
        services.AddSingleton<TranscriptionQueue>();
        services.AddSingleton<ITranscriptionService, TranscriptionService>();
        services.AddHostedService<TranscriptionBackgroundService>();

        // Departments
        services.AddScoped<IDepartmentService, DepartmentService>();

        return services;
    }

    /// <summary>
    /// Конфигурация JSON-сериализации
    /// </summary>
    public static IServiceCollection AddMessengerJson(this IServiceCollection services, IWebHostEnvironment environment)
    {
        void configureJson(System.Text.Json.JsonSerializerOptions options)
        {
            options.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
            options.WriteIndented = environment.IsDevelopment();
        }

        services.ConfigureHttpJsonOptions(options => configureJson(options.SerializerOptions));

        services.AddControllers().AddJsonOptions(options => configureJson(options.JsonSerializerOptions));

        return services;
    }
}