using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Options;
using OrderService.Application;
using OrderService.Contracts;

namespace OrderService.Infrastructure.Messaging;

public sealed class FulfillmentCapacityFailedConsumer : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly KafkaOptions _options;
    private readonly ILogger<FulfillmentCapacityFailedConsumer> _logger;

    public FulfillmentCapacityFailedConsumer(
        IServiceScopeFactory scopeFactory,
        IOptions<KafkaOptions> options,
        ILogger<FulfillmentCapacityFailedConsumer> logger)
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
        consumer.Subscribe(_options.Topics.FulfillmentCapacityFailed);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = consumer.Consume(stoppingToken);
                var envelope = JsonSerializer.Deserialize<IntegrationEventEnvelope<SagaFailurePayload>>(result.Message.Value, JsonOptions);

                if (envelope is null)
                {
                    consumer.Commit(result);
                    continue;
                }

                using var scope = _scopeFactory.CreateScope();
                var processManager = scope.ServiceProvider.GetRequiredService<OrderProcessManager>();
                var ev = new FulfillmentCapacityReservationFailedIntegrationEvent(envelope.EventId, envelope.Payload.OrderId, envelope.Payload.Reason ?? "Unknown");
                await processManager.HandleAsync(ev, stoppingToken);
                consumer.Commit(result);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to consume topic {Topic}", _options.Topics.FulfillmentCapacityFailed);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        consumer.Close();
    }
}
