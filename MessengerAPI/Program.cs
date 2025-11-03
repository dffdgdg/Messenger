using MessengerAPI.Model;
using MessengerAPI.Hubs;
using MessengerAPI.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using System.IO;
using MessengerAPI.Middleware;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<MessengerDbContext>(options => options.UseNpgsql(
    builder.Configuration.GetConnectionString("DefaultConnection"),
    o => {
        o.MapEnum<MessengerAPI.Model.Theme>("theme");
        o.MapEnum<MessengerAPI.Model.ChatRole>("chat_role");
    }
));

// Add JWT Authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = TokenHelper.GetValidationParameters();
  
        // Configure для SignalR
        options.Events = new JwtBearerEvents
        {
     OnMessageReceived = context =>
     {
                var accessToken = context.Request.Query["access_token"];
        var path = context.HttpContext.Request.Path;
     
       if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/chatHub"))
      {
    context.Token = accessToken;
      }
     return Task.CompletedTask;
            }
        };
    });

// Add authorization
builder.Services.AddAuthorizationBuilder()
                        // Add authorization
                        .SetFallbackPolicy(new AuthorizationPolicyBuilder()
  .RequireAuthenticatedUser()
        .Build());

builder.Services.AddSignalR();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add CORS for local development
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(
      policy =>
     {
   policy.AllowAnyHeader()
      .AllowAnyMethod()
        .SetIsOriginAllowed((host) => true)
         .AllowCredentials();
     });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// IMPORTANT: FileProtectionMiddleware must be before UseStaticFiles
app.UseFileProtection();

// Configure static files
var avatarPath = Path.Combine(Directory.GetCurrentDirectory(), "avatars");
if (!Directory.Exists(avatarPath))
{
    Directory.CreateDirectory(avatarPath);
}

// Configure MIME type provider
var provider = new FileExtensionContentTypeProvider();
provider.Mappings[".jpg"] = "image/jpeg";
provider.Mappings[".jpeg"] = "image/jpeg";
provider.Mappings[".png"] = "image/png";
provider.Mappings[".gif"] = "image/gif";

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(avatarPath),
    RequestPath = "/avatars",
    ContentTypeProvider = provider,
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers.Append("Cache-Control", "no-cache, no-store");
        ctx.Context.Response.Headers.Append("Pragma", "no-cache");
        ctx.Context.Response.Headers.Append("Expires", "-1");
    }
});

// Enable CORS
app.UseCors();

// Add authentication middleware
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<ChatHub>("/chatHub");

app.Run();
