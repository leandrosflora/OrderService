using System.Text.Json.Serialization;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using OrderService.Api;
using OrderService.Application;
using OrderService.Application.Ports;
using OrderService.Infrastructure.Messaging;
using OrderService.Infrastructure.Outbox;
using OrderService.Infrastructure.Persistence;

using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);
var serviceName = builder.Environment.ApplicationName;
var otlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"] ?? "http://localhost:5107";

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(serviceName))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint)))
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddPrometheusExporter());

builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddDbContext<OrderDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("OrderDb")
        ?? "Host=localhost;Port=5432;Database=logistica_envios;Username=logistica;Password=logistica;Search Path=order_domain,public";

    options.UseNpgsql(connectionString);
});

builder.Services.AddScoped<OrderProcessManager>();
builder.Services.AddScoped<OrderCancellationService>();
builder.Services.Configure<KafkaOptions>(builder.Configuration.GetSection(KafkaOptions.SectionName));
builder.Services.AddSingleton<IProducer<string, string>>(serviceProvider =>
{
    var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<KafkaOptions>>().Value;
    return new ProducerBuilder<string, string>(new ProducerConfig
    {
        BootstrapServers = options.BootstrapServers,
        Acks = Acks.All,
        EnableIdempotence = true,
        MessageSendMaxRetries = 3,
        RetryBackoffMs = 250
    }).Build();
});

builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<IOutboxWriter, OutboxWriter>();
builder.Services.AddScoped<IIntegrationEventBus, KafkaIntegrationEventBus>();

builder.Services.AddHostedService<OutboxDispatcher>();
builder.Services.AddHostedService<ShipmentStatusUpdatedConsumer>();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<OrderDbContext>();

var app = builder.Build();

app.UseOpenTelemetryPrometheusScrapingEndpoint("/metrics");

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapHealthChecks("/health");
app.MapOrderEndpoints();

app.Run();
