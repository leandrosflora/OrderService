using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Options;
using OrderService.Application;
using OrderService.Contracts;

namespace OrderService.Infrastructure.Messaging;

public sealed class InventoryReservedConsumer : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly KafkaOptions _options;
    private readonly ILogger<InventoryReservedConsumer> _logger;

    public InventoryReservedConsumer(
        IServiceScopeFactory scopeFactory,
        IOptions<KafkaOptions> options,
        ILogger<InventoryReservedConsumer> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        var config = new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = _options.ConsumerGroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe([_options.Topics.InventoryReserved, _options.Topics.InventoryReservationConfirmed]);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = consumer.Consume(stoppingToken);
                var envelope = JsonSerializer.Deserialize<IntegrationEventEnvelope<SagaPayload>>(result.Message.Value, JsonOptions);

                if (envelope is null)
                {
                    consumer.Commit(result);
                    continue;
                }

                using var scope = _scopeFactory.CreateScope();
                var processManager = scope.ServiceProvider.GetRequiredService<OrderProcessManager>();

                if (envelope.EventType == "inventory.reserved")
                {
                    var ev = new InventoryReservedIntegrationEvent(envelope.EventId, envelope.Payload.OrderId, envelope.Payload.ReservationId);
                    await processManager.HandleAsync(ev, stoppingToken);
                }
                else if (envelope.EventType == "inventory.reservation.confirmed")
                {
                    var ev = new InventoryReservationConfirmedIntegrationEvent(envelope.EventId, envelope.Payload.OrderId);
                    await processManager.HandleAsync(ev, stoppingToken);
                }
                else
                {
                    _logger.LogWarning("Unhandled event type {EventType} on {Topic}", envelope.EventType, result.Topic);
                }

                consumer.Commit(result);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to consume inventory reserved events");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        consumer.Close();
    }
}
