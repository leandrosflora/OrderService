using Microsoft.EntityFrameworkCore;
using OrderService.Infrastructure.Messaging;
using OrderService.Infrastructure.Persistence;

namespace OrderService.Infrastructure.Outbox;

public sealed class OutboxDispatcher : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxDispatcher> _logger;

    public OutboxDispatcher(IServiceScopeFactory scopeFactory, ILogger<OutboxDispatcher> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await DispatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Outbox dispatch cycle failed");
            }
        }
    }

    private async Task DispatchAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
        var eventBus = scope.ServiceProvider.GetRequiredService<IIntegrationEventBus>();
        var now = DateTimeOffset.UtcNow;

        var messages = await dbContext.OutboxMessages
            .Where(x => x.ProcessedAt == null)
            .Where(x => x.NextAttemptAt == null || x.NextAttemptAt <= now)
            .OrderBy(x => x.CreatedAt)
            .Take(100)
            .ToListAsync(cancellationToken);

        foreach (var message in messages)
        {
            try
            {
                await eventBus.PublishAsync(
                    message.Topic,
                    message.AggregateKey,
                    message.Payload,
                    message.MessageType,
                    cancellationToken);

                message.MarkProcessed();
            }
            catch (Exception exception)
            {
                message.MarkFailed(exception.Message);
                _logger.LogWarning(exception, "Failed to publish outbox message {MessageId}", message.Id);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
