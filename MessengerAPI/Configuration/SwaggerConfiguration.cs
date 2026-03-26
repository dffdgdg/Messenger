using Microsoft.OpenApi;

namespace MessengerAPI.Configuration;

public static class SwaggerConfiguration
{
    public static IServiceCollection AddMessengerSwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();

        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Messenger API",
                Version = "v1"
            });

            c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Description = "JWT Authorization header. Example: \"Authorization: Bearer {token}\"",
                Name = "Authorization",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.ApiKey,
                Scheme = "Bearer"
            });

            c.AddSecurityRequirement(_ =>
            {
                var schemeRef = new OpenApiSecuritySchemeReference("Bearer");

                return new OpenApiSecurityRequirement
                {
                    [schemeRef] = []
                };
            });
        });

        return services;
    }
}