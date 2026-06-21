using API.Application.Services.Abstractions;
using API.Application.Services.Core.Auth;
using API.Application.Services.Features.Call;
using API.Application.Services.Features.Chat;
using API.Application.Services.Features.Department;
using API.Application.Services.Features.Messaging;
using API.Application.Services.Features.ReadReceipt;
using API.Application.Services.Features.User;
using API.Services.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace API.Application;

public static class ApplicationExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services
            .AddAuthServices()
            .AddChatServices()
            .AddMessagingServices()
            .AddUserServices()
            .AddCallServices();

        return services;
    }

    private static IServiceCollection AddAuthServices(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ITokenService, TokenService>();

        return services;
    }

    private static IServiceCollection AddChatServices(this IServiceCollection services)
    {
        services.AddScoped<IChatService, ChatService>();
        services.AddScoped<IChatMemberService, ChatMemberService>();
        services.AddScoped<ISystemMessageService, SystemMessageService>();
        services.AddScoped<INotificationService, NotificationService>();

        return services;
    }

    private static IServiceCollection AddMessagingServices(this IServiceCollection services)
    {
        services.AddScoped<IMessageService, MessageService>();
        services.AddScoped<IPollService, PollService>();
        services.AddScoped<IReadReceiptService, ReadReceiptService>();
        services.AddScoped<IFileService, FileService>();

        return services;
    }

    private static IServiceCollection AddUserServices(this IServiceCollection services)
    {
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IAdminService, AdminService>();
        services.AddScoped<IDepartmentService, DepartmentService>();

        return services;
    }

    private static IServiceCollection AddCallServices(this IServiceCollection services)
    {
        services.AddSingleton<CallMixerService>();
        services.AddSingleton<CallRelayService>();
        services.AddHostedService(sp => sp.GetRequiredService<CallRelayService>());
        services.AddSingleton<ICallSessionService, CallSessionService>();

        return services;
    }
}