using API.Services.Auth;
using API.Services.Call;
using API.Services.Chat;
using API.Services.Core.Auth;
using API.Services.Department;
using API.Services.Features.Chat;
using API.Services.Infrastructure;
using API.Services.Infrastructure.Database;
using API.Services.Infrastructure.Network;
using API.Services.Infrastructure.Status;
using API.Services.Messaging;
using API.Services.ReadReceipt;
using API.Services.User;

namespace API.Configuration;

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
                npgsql.MapEnum<Theme>("theme");
                npgsql.MapEnum<ChatRole>("chat_role", nameTranslator: (Npgsql.INpgsqlNameTranslator?)EnumTypeMappings.ChatRoleNameTranslator);
                npgsql.MapEnum<ChatType>("chat_type", nameTranslator: (Npgsql.INpgsqlNameTranslator?)EnumTypeMappings.ChatTypeNameTranslator);
                npgsql.MapEnum<SystemEventType>("system_event_type");
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
    /// Инфраструктурные сервисы
    /// </summary>
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddHttpContextAccessor();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<AppDateTime>();
        services.AddSingleton<ICallSessionService, CallSessionService>();
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
        services.AddScoped<IAuthService, AuthService>();

        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IAdminService, AdminService>();
        services.AddScoped<IUserStatusService, UserStatusService>();
        services.AddHostedService<StatusCleanupHostedService>();

        services.AddScoped<IChatService, ChatService>();
        services.AddScoped<IChatMemberService, ChatMemberService>();
        services.AddScoped<ISystemMessageService, SystemMessageService>();
        services.AddScoped<INotificationService, NotificationService>();

        services.AddScoped<IMessageService, MessageService>();
        services.AddScoped<IPollService, PollService>();
        services.AddScoped<IReadReceiptService, ReadReceiptService>();

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