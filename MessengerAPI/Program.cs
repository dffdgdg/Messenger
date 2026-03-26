using MessengerAPI.Hubs;
using MessengerAPI.Middleware;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<MessengerSettings>(builder.Configuration.GetSection(MessengerSettings.SectionName));
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection(JwtSettings.SectionName));

builder.Services.AddMessengerDatabase(builder.Configuration, builder.Environment).AddInfrastructureServices().AddBusinessServices()
    .AddMessengerJson(builder.Environment).AddMessengerAuth(builder.Configuration).AddMessengerSwagger();

builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: RateLimitKey.GetIpPartitionKey(context),
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
            partitionKey: RateLimitKey.GetIpPartitionKey(context),
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
            partitionKey: RateLimitKey.GetUserOrIpPartitionKey(context),
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
            partitionKey: RateLimitKey.GetUserOrIpPartitionKey(context),
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
            partitionKey: RateLimitKey.GetUserOrIpPartitionKey(context),
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
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.ContentType = "application/json";

        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfterValue)
            ? retryAfterValue
            : TimeSpan.FromSeconds(10);

        context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();

        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            success = false,
            error = "Слишком много запросов. Попробуйте позже.",
            retryAfterSeconds = (int)retryAfter.TotalSeconds,
            timestamp = DateTime.UtcNow
        }, cancellationToken);
    };
});

builder.Services.AddSignalR();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy
    => policy.AllowAnyHeader().AllowAnyMethod().SetIsOriginAllowed(_ => true).AllowCredentials()));

var app = builder.Build();

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
app.UseMissingFileCleanup();
app.UseCors();

app.UseRateLimiter();

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