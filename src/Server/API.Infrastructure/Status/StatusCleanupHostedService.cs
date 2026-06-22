using API.Application.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace API.Infrastructure.Status;

public class StatusCleanupHostedService(IServiceScopeFactory scopeFactory,
    ILogger<StatusCleanupHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var statusService = scope.ServiceProvider.GetRequiredService<IUserStatusService>();
                await statusService.CleanupExpiredStatusesAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error cleaning up expired statuses");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}