using System.Threading.Channels;

namespace MessengerAPI.Services.Messaging;

public class TranscriptionQueue
{
    private readonly Channel<int> _channel =
        Channel.CreateUnbounded<int>(new UnboundedChannelOptions
        {
            SingleReader = true
        });

    public async Task EnqueueAsync(int messageId, CancellationToken ct = default)
        => await _channel.Writer.WriteAsync(messageId, ct);

    public IAsyncEnumerable<int> DequeueAllAsync(CancellationToken ct)
        => _channel.Reader.ReadAllAsync(ct);
}

public class TranscriptionBackgroundService(TranscriptionQueue queue,IServiceScopeFactory scopeFactory,
    ILogger<TranscriptionBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Сервис транскрибации запущен");

        await foreach (var messageId in
            queue.DequeueAllAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<ITranscriptionService>();

                var result = await service.TranscribeAsync(messageId, stoppingToken);

                if (result.IsFailure)
                    logger.LogWarning("Транскрибация {MessageId}: {Error}",messageId, result.Error);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex,"Ошибка транскрибации {MessageId}", messageId);
            }
        }
    }
}