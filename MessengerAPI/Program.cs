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
builder.Services.AddCors(options => options.AddDefaultPolicy(policy 
    => policy.AllowAnyHeader().AllowAnyMethod().SetIsOriginAllowed(_ => true).AllowCredentials()));

var app = builder.Build();

// Middleware pipeline
app.UseExceptionHandling();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

var disableHttpsRedirection = app.Configuration.GetValue<bool>("DisableHttpsRedirection");
if (!disableHttpsRedirection)
{
    app.UseHttpsRedirection();
}
app.Use(async (context, next) =>
{
    context.Response.Headers.TryAdd("Cross-Origin-Embedder-Policy", "require-corp");
    context.Response.Headers.TryAdd("Cross-Origin-Opener-Policy", "same-origin");
    context.Response.Headers.TryAdd("Cross-Origin-Resource-Policy", "same-origin");
    context.Response.Headers.TryAdd("X-Content-Type-Options", "nosniff");

    await next();
});

app.UseMessengerStaticFiles();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", (HttpContext context) =>
{
    context.Response.Headers.CacheControl = "public,max-age=300";
    return Results.Text("Messenger API is running");
}).AllowAnonymous();

app.MapGet("/robots.txt", (HttpContext context) =>
{
    context.Response.Headers.CacheControl = "public,max-age=300";
    return Results.Text("User-agent: *\nDisallow:", "text/plain");
}).AllowAnonymous();

app.MapGet("/sitemap.xml", (HttpContext context) =>
{
    context.Response.Headers.CacheControl = "public,max-age=300";
    return Results.Text("""<?xml version="1.0" encoding="UTF-8"?><urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9"></urlset>""", "application/xml");
}).AllowAnonymous();

app.MapControllers();
app.MapHub<ChatHub>("/chatHub");

await app.RunAsync();