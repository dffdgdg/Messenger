using API.Application.Services.Abstractions;
using API.Domain.Repositories;
using API.Infrastructure.Cache;
using API.Infrastructure.Database;
using API.Infrastructure.Database.SeedData;
using API.Infrastructure.Network;
using API.Infrastructure.Repositories.Implementations;
using API.Infrastructure.Security;
using API.Infrastructure.Status;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shared.Enum;

namespace API.Infrastructure;

public static class InfrastructureExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
    {
        services.AddDatabase(configuration, environment).AddRepositories().AddInfrastructureServices();

        return services;
    }

    private static IServiceCollection AddDatabase(this IServiceCollection services,IConfiguration configuration,IWebHostEnvironment environment)
    {
        services.AddDbContext<MessengerDbContext>(options =>
        {
            options.UseNpgsql(configuration.GetConnectionString("DefaultConnection"), npgsql =>
            {
                npgsql.MapEnum<Theme>("theme");
                npgsql.MapEnum<ChatRole>("chat_role",
                    nameTranslator: EnumTypeMappings.ChatRoleNameTranslator);
                npgsql.MapEnum<ChatType>("chat_type",
                    nameTranslator: EnumTypeMappings.ChatTypeNameTranslator);
                npgsql.MapEnum<SystemEventType>("system_event_type");
                npgsql.MapEnum<UserStatusType>("user_status_type",
                    nameTranslator: EnumTypeMappings.UserStatusTypeNameTranslator);
                npgsql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
                npgsql.MaxBatchSize(100);
            });

            if (environment.IsDevelopment())
            {
                options.EnableSensitiveDataLogging();
                options.EnableDetailedErrors();
            }
        });

        services.AddScoped<DataSeeder>();

        return services;
    }

    private static IServiceCollection AddRepositories(this IServiceCollection services)
    {
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IChatRepository, ChatRepository>();
        services.AddScoped<IMessageRepository, MessageRepository>();
        services.AddScoped<IReadReceiptRepository, ReadReceiptRepository>();
        services.AddScoped<IPollRepository, PollRepository>();
        services.AddScoped<IDepartmentRepository, DepartmentRepository>();

        return services;
    }

    private static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<ICacheService, CacheService>();
        services.AddScoped<IAccessControlService, AccessControlService>();
        services.AddScoped<IUrlBuilder, HttpUrlBuilder>();

        services.AddSingleton<IOnlineUserService, OnlineUserService>();
        services.AddScoped<IUserStatusService, UserStatusService>();
        services.AddHostedService<StatusCleanupHostedService>();

        return services;
    }
}