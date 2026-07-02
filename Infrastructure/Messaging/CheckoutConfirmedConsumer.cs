using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Options;
using OrderService.Application;
using OrderService.Contracts;

namespace OrderService.Infrastructure.Messaging;

public sealed class CheckoutConfirmedConsumer : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly KafkaOptions _options;
    private readonly ILogger<CheckoutConfirmedConsumer> _logger;

    public CheckoutConfirmedConsumer(
        IServiceScopeFactory scopeFactory,
        IOptions<KafkaOptions> options,
        ILogger<CheckoutConfirmedConsumer> logger)
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
        consumer.Subscribe(_options.Topics.CheckoutConfirmed);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = consumer.Consume(stoppingToken);
                var envelope = JsonSerializer.Deserialize<IntegrationEventEnvelope<CheckoutConfirmedPayload>>(result.Message.Value, JsonOptions);

                if (envelope is null || envelope.EventType != "checkout.confirmed")
                {
                    _logger.LogWarning("Ignoring Kafka message from topic {Topic} with key {Key}: unsupported event envelope", result.Topic, result.Message.Key);
                    consumer.Commit(result);
                    continue;
                }

                _logger.LogInformation(
                    "Consumed Kafka message from topic {Topic} with key {Key}, eventId {EventId}, correlationId {CorrelationId}",
                    result.Topic,
                    result.Message.Key,
                    envelope.EventId,
                    envelope.CorrelationId);

                var integrationEvent = MapToIntegrationEvent(envelope);

                using var scope = _scopeFactory.CreateScope();
                var processManager = scope.ServiceProvider.GetRequiredService<OrderProcessManager>();
                await processManager.HandleAsync(integrationEvent, stoppingToken);
                consumer.Commit(result);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to consume Kafka topic {Topic}", _options.Topics.CheckoutConfirmed);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        consumer.Close();
    }

    private static CheckoutConfirmedIntegrationEvent MapToIntegrationEvent(IntegrationEventEnvelope<CheckoutConfirmedPayload> envelope)
    {
        var payload = envelope.Payload;
        return new CheckoutConfirmedIntegrationEvent(
            MessageId: envelope.EventId,
            OccurredAt: envelope.OccurredAt,
            CheckoutId: payload.CheckoutId,
            BuyerId: payload.BuyerId,
            SellerId: payload.SellerId,
            Currency: payload.Currency,
            ShippingPrice: payload.ShippingPrice,
            ShippingPromiseId: payload.ShippingPromiseId,
            // checkout.confirmed carries no pricing quote id; Order.Create requires a non-empty
            // one, so a placeholder is generated here (Guid.Empty previously made every order
            // creation throw and left the consumer stuck retrying the same offset forever).
            PricingQuoteId: Guid.NewGuid(),
            PaymentMethodToken: payload.PaymentMethodToken,
            Items: payload.Items
                .Select(i => new CheckoutConfirmedItem(
                    i.SkuId,
                    $"SKU {i.SkuId:N}",
                    i.Quantity,
                    i.UnitPrice,
                    // checkout.confirmed carries no per-item fulfillment center id (checkout
                    // never selects one); ReservationItem rejects Guid.Empty, so fall back to
                    // the seeded demo fulfillment center until checkout actually resolves this
                    // per SKU via ProductCatalogService/InventoryService.
                    DemoFulfillmentCenterId))
                .ToList());
    }

    private static readonly Guid DemoFulfillmentCenterId = Guid.Parse("33333333-3333-3333-3333-333333333333");
}

internal sealed record CheckoutConfirmedPayload(
    Guid CheckoutId,
    Guid BuyerId,
    Guid SellerId,
    string Currency,
    decimal ShippingPrice,
    string ShippingPromiseId,
    string PaymentMethodToken,
    IReadOnlyList<CheckoutConfirmedItemPayload> Items);

internal sealed record CheckoutConfirmedItemPayload(
    Guid SkuId,
    int Quantity,
    decimal UnitPrice);
