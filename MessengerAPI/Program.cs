using MessengerAPI.Configuration;
using MessengerAPI.Hubs;
using MessengerAPI.Middleware;

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

// Configuration binding
builder.Services.Configure<MessengerSettings>(builder.Configuration.GetSection(MessengerSettings.SectionName));
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection(JwtSettings.SectionName));

// Services
builder.Services
    .AddMessengerDatabase(builder.Configuration, builder.Environment)
    .AddInfrastructureServices()
    .AddBusinessServices()
    .AddMessengerJson(builder.Environment)
    .AddMessengerAuth(builder.Configuration)
    .AddMessengerSwagger();

// SignalR & CORS
builder.Services.AddSignalR();
builder.Services.AddCors
    (options => options.AddDefaultPolicy(policy => policy.AllowAnyHeader().AllowAnyMethod().SetIsOriginAllowed(_ => true).AllowCredentials()));

var app = builder.Build();

// Middleware pipeline
app.UseExceptionHandling();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseMessengerStaticFiles();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<ChatHub>("/chatHub");

await app.RunAsync();