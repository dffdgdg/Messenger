using API.Application.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace API.Infrastructure.Services.Features.Messaging;

public class PendingUploadCleanupHostedService(IServiceScopeFactory scopeFactory,
    ILogger<PendingUploadCleanupHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var store = scope.ServiceProvider.GetRequiredService<IPendingUploadStore>();

                foreach (var upload in store.GetExpired(DateTime.UtcNow))
                {
                    store.Remove(upload.Token);
                    TryDeleteFile(upload.AbsolutePath);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Ошибка очистки незавершённых загрузок");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    private void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Не удалось удалить неиспользованный файл {Path}", path);
        }
    }
}