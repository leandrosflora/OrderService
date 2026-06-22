using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Options;
using OrderService.Application;
using OrderService.Contracts;

namespace OrderService.Infrastructure.Messaging;

public sealed class ShipmentCreatedConsumer : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly KafkaOptions _options;
    private readonly ILogger<ShipmentCreatedConsumer> _logger;

    public ShipmentCreatedConsumer(
        IServiceScopeFactory scopeFactory,
        IOptions<KafkaOptions> options,
        ILogger<ShipmentCreatedConsumer> logger)
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
        consumer.Subscribe([_options.Topics.ShipmentCreated, _options.Topics.ShipmentCreationFailed]);

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

                if (envelope.EventType == "shipment.created")
                {
                    var ev = new ShipmentCreatedIntegrationEvent(envelope.EventId, envelope.Payload.OrderId, envelope.Payload.ReservationId);
                    await processManager.HandleAsync(ev, stoppingToken);
                }
                else if (envelope.EventType == "shipment.creation.failed")
                {
                    var failPayload = JsonSerializer.Deserialize<IntegrationEventEnvelope<SagaFailurePayload>>(result.Message.Value, JsonOptions);
                    if (failPayload is not null)
                    {
                        var ev = new ShipmentCreationFailedIntegrationEvent(failPayload.EventId, failPayload.Payload.OrderId, failPayload.Payload.Reason ?? "Unknown");
                        await processManager.HandleAsync(ev, stoppingToken);
                    }
                }

                consumer.Commit(result);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to consume shipment created events");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        consumer.Close();
    }
}
