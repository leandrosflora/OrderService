using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Options;
using OrderService.Application;
using OrderService.Contracts;

namespace OrderService.Infrastructure.Messaging;

public sealed class ShipmentStatusUpdatedConsumer : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly KafkaOptions _options;
    private readonly ILogger<ShipmentStatusUpdatedConsumer> _logger;

    public ShipmentStatusUpdatedConsumer(
        IServiceScopeFactory scopeFactory,
        IOptions<KafkaOptions> options,
        ILogger<ShipmentStatusUpdatedConsumer> logger)
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
        consumer.Subscribe(_options.Topics.ShipmentStatusUpdated);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = consumer.Consume(stoppingToken);
                var envelope = JsonSerializer.Deserialize<IntegrationEventEnvelope<ShipmentStatusUpdatedIntegrationEvent>>(result.Message.Value, JsonOptions);

                if (envelope is null || envelope.EventType != "shipment.status.updated")
                {
                    _logger.LogWarning("Ignoring Kafka message from topic {Topic} with key {Key}: unsupported event envelope", result.Topic, result.Message.Key);
                    consumer.Commit(result);
                    continue;
                }

                _logger.LogInformation(
                    "Consumed Kafka message from topic {Topic} with key {Key}, eventType {EventType}, correlationId {CorrelationId}",
                    result.Topic,
                    result.Message.Key,
                    envelope.EventType,
                    envelope.CorrelationId);

                using var scope = _scopeFactory.CreateScope();
                var processManager = scope.ServiceProvider.GetRequiredService<OrderProcessManager>();
                await processManager.HandleShipmentStatusUpdatedAsync(envelope.EventId, envelope.Payload, stoppingToken);
                consumer.Commit(result);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to consume Kafka topic {Topic}", _options.Topics.ShipmentStatusUpdated);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        consumer.Close();
    }
}
