using API.Data.SeedData;
using API.Hubs;
using API.Middleware;
using API.Services;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

var messengerSettings = builder.Configuration
    .GetSection(MessengerSettings.SectionName)
    .Get<MessengerSettings>() ?? new MessengerSettings();
var maxUploadRequestBodySize = messengerSettings.MaxFileSizeBytes + 1024L * 1024L;

builder.WebHost.ConfigureKestrel(options =>
{
options.Limits.MaxRequestBodySize = maxUploadRequestBodySize;
options.Listen(System.Net.IPAddress.Any, 5274,
        listenOptions => listenOptions.Protocols = HttpProtocols.Http1AndHttp2);
});

builder.Services.Configure<FormOptions>(options =>
    options.MultipartBodyLengthLimit = messengerSettings.MaxFileSizeBytes);

builder.Services.Configure<IISServerOptions>(options =>
    options.MaxRequestBodySize = maxUploadRequestBodySize);


builder.Services.Configure<MessengerSettings>(
    builder.Configuration.GetSection(MessengerSettings.SectionName));

builder.Services.Configure<JwtSettings>(
    builder.Configuration.GetSection(JwtSettings.SectionName));

builder.Services
    .AddMessengerDatabase(builder.Configuration, builder.Environment)
    .AddInfrastructureServices()
    .AddBusinessServices()
    .AddMessengerJson(builder.Environment)
    .AddMessengerAuth(builder.Configuration)
    .AddMessengerSwagger();

builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetSlidingWindowLimiter(
            RateLimitKey.GetIpPartitionKey(context),
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromSeconds(10),
                SegmentsPerWindow = 5,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 5
            }));

    options.AddPolicy("login", context =>
        RateLimitPartition.GetSlidingWindowLimiter(
            RateLimitKey.GetIpPartitionKey(context),
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 3,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));

    options.AddPolicy("upload", context =>
        RateLimitPartition.GetSlidingWindowLimiter(
            RateLimitKey.GetUserOrIpPartitionKey(context),
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 3,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 2
            }));

    options.AddPolicy("search", context =>
        RateLimitPartition.GetSlidingWindowLimiter(
            RateLimitKey.GetUserOrIpPartitionKey(context),
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 15,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 3,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));

    options.AddPolicy("messaging", context =>
        RateLimitPartition.GetSlidingWindowLimiter(
            RateLimitKey.GetUserOrIpPartitionKey(context),
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 6,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 3
            }));

    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode =
            StatusCodes.Status429TooManyRequests;

        context.HttpContext.Response.ContentType = "application/json";

        var retryAfter =
            context.Lease.TryGetMetadata(MetadataName.RetryAfter,
                out var retryAfterValue)
                ? retryAfterValue
                : TimeSpan.FromSeconds(10);

        context.HttpContext.Response.Headers.RetryAfter =
            ((int)retryAfter.TotalSeconds).ToString();

        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            success = false,
            error = "Слишком много запросов. Попробуйте позже.",
            retryAfterSeconds = (int)retryAfter.TotalSeconds,
            timestamp = DateTime.UtcNow
        }, cancellationToken);
    };
});

builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = true;
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
    options.HandshakeTimeout = TimeSpan.FromSeconds(15);
});

builder.Services.AddHostedService<UdpDiscoveryService>();

builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyHeader()
              .AllowAnyMethod()
              .SetIsOriginAllowed(_ => true)
              .AllowCredentials()));

var app = builder.Build();

app.UseExceptionHandling();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<MessengerDbContext>();

    await db.Database.MigrateAsync();

    var seeder = scope.ServiceProvider.GetRequiredService<DataSeeder>();

    await seeder.SeedAsync();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

var disableHttpsRedirection =
    app.Configuration.GetValue<bool>("DisableHttpsRedirection");

if (!disableHttpsRedirection)
{
    app.UseHttpsRedirection();
}

app.Use(async (context, next) =>
{
    context.Response.Headers.TryAdd("X-Content-Type-Options", "nosniff");
    await next();
});

app.UseMessengerStaticFiles();
app.UseMissingFileCleanup();

app.UseCors();

var isHttpInDocker =
    app.Configuration.GetValue<bool>("DisableHttpsRedirection");

app.UseCookiePolicy(new CookiePolicyOptions
{
    MinimumSameSitePolicy = SameSiteMode.Lax,
    HttpOnly = Microsoft.AspNetCore.CookiePolicy.HttpOnlyPolicy.Always,
    Secure = isHttpInDocker
        ? CookieSecurePolicy.None
        : CookieSecurePolicy.SameAsRequest
});

app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(30)
});

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () =>
    Results.Text("Messenger API is running"))
    .AllowAnonymous();

app.MapHub<MessengerHub>("/chatHub");

app.MapControllers();

await app.RunAsync();