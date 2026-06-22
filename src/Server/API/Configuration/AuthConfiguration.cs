using API.Application.Services.Core.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Shared.Enum;
using System.Security.Claims;

namespace API.Web.Configuration;

public static class AuthConfiguration
{
    public static IServiceCollection AddMessengerAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = TokenService.CreateValidationParameters(configuration);

                options.RequireHttpsMetadata = false;

                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(accessToken) &&
                            context.HttpContext.Request.Path
                                .StartsWithSegments("/chatHub"))
                        {
                            context.Token = accessToken;
                        }
                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorizationBuilder().SetFallbackPolicy(new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser().Build())
            .AddPolicy("IsAdmin", policy =>
            policy.RequireAssertion(context =>
            {
                var roleClaim = context.User.FindFirst(ClaimTypes.Role)?.Value;
                return int.TryParse(roleClaim, out var roleInt) && ((UserRole)roleInt).HasFlag(UserRole.Admin);
            }));

        return services;
    }
}