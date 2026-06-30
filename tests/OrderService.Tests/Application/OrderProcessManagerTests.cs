using Microsoft.Data.Sqlite;
using Xunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OrderService.Application;
using OrderService.Contracts;
using OrderService.Domain;
using OrderService.Infrastructure.Messaging;
using OrderService.Infrastructure.Persistence;
using OrderService.Tests.Fakes;

namespace OrderService.Tests.Application;

public sealed class OrderProcessManagerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OrderDbContext _db;
    private readonly FakeOutboxWriter _outbox;
    private readonly OrderProcessManager _sut;

    public OrderProcessManagerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new OrderDbContext(options);
        _db.Database.EnsureCreated();

        _outbox = new FakeOutboxWriter();

        var kafkaOptions = Options.Create(new KafkaOptions());
        var env = new ProductionEnvironment();

        _sut = new OrderProcessManager(
            _db,
            _outbox,
            kafkaOptions,
            env,
            NullLogger<OrderProcessManager>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    // ─── CheckoutConfirmed ───────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_CheckoutConfirmed_CreatesOrderAndEnqueuesThreeCommands()
    {
        var evt = CheckoutEvent();

        await _sut.HandleAsync(evt, CancellationToken.None);

        _db.ChangeTracker.Clear();
        var order = await _db.Orders.Include(x => x.Items).SingleAsync(x => x.CheckoutId == evt.CheckoutId);

        Assert.Equal(evt.BuyerId, order.BuyerId);
        Assert.Equal(evt.SellerId, order.SellerId);
        Assert.Equal("BRL", order.Currency);
        Assert.Equal(OrderStatus.ReservingResources, order.Status);
        Assert.Single(order.Items);

        Assert.Equal(3, _outbox.Calls.Count);
        Assert.Single(_outbox.CallsFor("order.created"));
        Assert.Single(_outbox.CallsFor("inventory.commands"));
        Assert.Single(_outbox.CallsFor("fulfillment.commands"));
    }

    [Fact]
    public async Task HandleAsync_CheckoutConfirmed_EnqueuesInventoryReservationForCorrectSeller()
    {
        var evt = CheckoutEvent();
        await _sut.HandleAsync(evt, CancellationToken.None);

        var cmd = _outbox.SingleMessage<ReserveInventoryCommand>("inventory.commands");

        Assert.NotNull(cmd);
        Assert.Equal(evt.SellerId, cmd.SellerId);
        Assert.Single(cmd.Items);
        Assert.Equal(evt.Items[0].SkuId, cmd.Items[0].SkuId);
        Assert.Equal(2, cmd.Items[0].Quantity);
    }

    [Fact]
    public async Task HandleAsync_CheckoutConfirmed_EnqueuesFulfillmentReservationWithCapacityUnits()
    {
        var fc = Guid.NewGuid();
        var evt = CheckoutEvent(fulfillmentCenterId: fc);
        await _sut.HandleAsync(evt, CancellationToken.None);

        var cmd = _outbox.SingleMessage<ReserveFulfillmentCapacityCommand>("fulfillment.commands");

        Assert.NotNull(cmd);
        Assert.Equal(fc, cmd.FulfillmentCenterId);
        Assert.Equal(2, cmd.CapacityUnits); // 2 items in checkout event
    }

    [Fact]
    public async Task HandleAsync_CheckoutConfirmed_DuplicateMessage_IsSkipped()
    {
        var evt = CheckoutEvent();
        await _sut.HandleAsync(evt, CancellationToken.None);
        _outbox.Reset();

        await _sut.HandleAsync(evt, CancellationToken.None);

        Assert.Empty(_outbox.Calls);
        Assert.Equal(1, await _db.Orders.CountAsync());
    }

    [Fact]
    public async Task HandleAsync_CheckoutConfirmed_WhenOrderAlreadyExistsForCheckout_DoesNotCreateDuplicate()
    {
        var checkoutId = Guid.NewGuid();
        var evt1 = CheckoutEvent(checkoutId: checkoutId);
        await _sut.HandleAsync(evt1, CancellationToken.None);
        _outbox.Reset();

        var evt2 = CheckoutEvent(checkoutId: checkoutId);
        await _sut.HandleAsync(evt2, CancellationToken.None);

        Assert.Equal(1, await _db.Orders.CountAsync());
        Assert.Empty(_outbox.Calls);
    }

    [Fact]
    public async Task HandleAsync_CheckoutConfirmed_AddsMessageToInbox()
    {
        var evt = CheckoutEvent();

        await _sut.HandleAsync(evt, CancellationToken.None);

        Assert.True(await _db.InboxMessages.AnyAsync(x => x.MessageId == evt.MessageId));
    }

    // ─── InventoryReserved ───────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_InventoryReserved_WhenCapacityNotYetReserved_NoPaymentCommand()
    {
        var orderId = await ProcessCheckoutAsync();
        _outbox.Reset();

        await _sut.HandleAsync(
            new InventoryReservedIntegrationEvent(Guid.NewGuid(), orderId, Guid.NewGuid()),
            CancellationToken.None);

        Assert.Empty(_outbox.CallsFor("payment.commands"));
        _db.ChangeTracker.Clear();
        var order = await _db.Orders.SingleAsync(x => x.Id == orderId);
        Assert.Equal(ReservationState.Reserved, order.InventoryState);
        Assert.Equal(OrderStatus.ReservingResources, order.Status);
    }

    [Fact]
    public async Task HandleAsync_InventoryReserved_IdempotentOnDuplicate()
    {
        var orderId = await ProcessCheckoutAsync();
        var evt = new InventoryReservedIntegrationEvent(Guid.NewGuid(), orderId, Guid.NewGuid());
        await _sut.HandleAsync(evt, CancellationToken.None);
        _outbox.Reset();

        await _sut.HandleAsync(evt, CancellationToken.None);

        Assert.Empty(_outbox.Calls);
    }

    // ─── FulfillmentCapacityReserved ─────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_FulfillmentCapacityReserved_WhenInventoryNotYetReserved_NoPaymentCommand()
    {
        var orderId = await ProcessCheckoutAsync();
        _outbox.Reset();

        await _sut.HandleAsync(
            new FulfillmentCapacityReservedIntegrationEvent(Guid.NewGuid(), orderId, Guid.NewGuid()),
            CancellationToken.None);

        Assert.Empty(_outbox.CallsFor("payment.commands"));
        _db.ChangeTracker.Clear();
        var order = await _db.Orders.SingleAsync(x => x.Id == orderId);
        Assert.Equal(ReservationState.Reserved, order.CapacityState);
        Assert.Equal(OrderStatus.ReservingResources, order.Status);
    }

    [Fact]
    public async Task HandleAsync_BothReserved_SendsPaymentAuthorizationCommand()
    {
        var orderId = await ProcessCheckoutAsync();
        await _sut.HandleAsync(new InventoryReservedIntegrationEvent(Guid.NewGuid(), orderId, Guid.NewGuid()), CancellationToken.None);
        _db.ChangeTracker.Clear();
        _outbox.Reset();

        await _sut.HandleAsync(new FulfillmentCapacityReservedIntegrationEvent(Guid.NewGuid(), orderId, Guid.NewGuid()), CancellationToken.None);

        var cmd = _outbox.SingleMessage<AuthorizePaymentCommand>("payment.commands");
        Assert.NotNull(cmd);
        Assert.Equal(orderId, cmd.OrderId);
        _db.ChangeTracker.Clear();
        var order = await _db.Orders.SingleAsync(x => x.Id == orderId);
        Assert.Equal(OrderStatus.AwaitingPaymentAuthorization, order.Status);
    }

    // ─── Reservation Failures ────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_InventoryReservationFailed_CancelsOrderAndWritesCompensations()
    {
        var orderId = await ProcessCheckoutAsync();
        _outbox.Reset();

        await _sut.HandleAsync(
            new InventoryReservationFailedIntegrationEvent(Guid.NewGuid(), orderId, "sem estoque"),
            CancellationToken.None);

        _db.ChangeTracker.Clear();
        var order = await _db.Orders.SingleAsync(x => x.Id == orderId);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Contains("sem estoque", order.CancellationReason);
        Assert.Single(_outbox.CallsFor("order.events"));
    }

    [Fact]
    public async Task HandleAsync_InventoryReservationFailed_WhenInventoryAlreadyReserved_WritesReleaseCommand()
    {
        var orderId = await ProcessCheckoutAsync();
        var invReservationId = Guid.NewGuid();
        await _sut.HandleAsync(new InventoryReservedIntegrationEvent(Guid.NewGuid(), orderId, invReservationId), CancellationToken.None);
        _db.ChangeTracker.Clear();
        _outbox.Reset();

        await _sut.HandleAsync(
            new FulfillmentCapacityReservationFailedIntegrationEvent(Guid.NewGuid(), orderId, "sem capacidade"),
            CancellationToken.None);

        var releaseCmd = _outbox.SingleMessage<ReleaseInventoryReservationCommand>("inventory.commands");
        Assert.NotNull(releaseCmd);
        Assert.Equal(invReservationId, releaseCmd.ReservationId);
    }

    [Fact]
    public async Task HandleAsync_FulfillmentCapacityReservationFailed_CancelsOrder()
    {
        var orderId = await ProcessCheckoutAsync();
        _outbox.Reset();

        await _sut.HandleAsync(
            new FulfillmentCapacityReservationFailedIntegrationEvent(Guid.NewGuid(), orderId, "sem capacidade"),
            CancellationToken.None);

        _db.ChangeTracker.Clear();
        var order = await _db.Orders.SingleAsync(x => x.Id == orderId);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    // ─── PaymentAuthorized ───────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_PaymentAuthorized_ConfirmsReservationsAndTransitions()
    {
        var orderId = await ProcessBothReservedAsync();
        var authId = Guid.NewGuid();
        _outbox.Reset();

        await _sut.HandleAsync(
            new PaymentAuthorizedIntegrationEvent(Guid.NewGuid(), orderId, authId),
            CancellationToken.None);

        Assert.Single(_outbox.CallsFor("inventory.commands"));
        Assert.Single(_outbox.CallsFor("fulfillment.commands"));
        var invCmd = _outbox.SingleMessage<ConfirmInventoryReservationCommand>("inventory.commands");
        var capCmd = _outbox.SingleMessage<ConfirmFulfillmentCapacityCommand>("fulfillment.commands");
        Assert.NotNull(invCmd);
        Assert.NotNull(capCmd);
        _db.ChangeTracker.Clear();
        var order = await _db.Orders.SingleAsync(x => x.Id == orderId);
        Assert.Equal(OrderStatus.ConfirmingResources, order.Status);
        Assert.Equal(authId, order.PaymentAuthorizationId);
    }

    // ─── PaymentAuthorizationFailed ──────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_PaymentAuthorizationFailed_CancelsAndCompensates()
    {
        var orderId = await ProcessBothReservedAsync();
        _outbox.Reset();

        await _sut.HandleAsync(
            new PaymentAuthorizationFailedIntegrationEvent(Guid.NewGuid(), orderId, "fraude"),
            CancellationToken.None);

        _db.ChangeTracker.Clear();
        var order = await _db.Orders.SingleAsync(x => x.Id == orderId);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        // Must release both reservations
        Assert.Single(_outbox.CallsFor("inventory.commands"));
        Assert.Single(_outbox.CallsFor("fulfillment.commands"));
        Assert.Single(_outbox.CallsFor("order.events"));
    }

    // ─── InventoryReservationConfirmed / FulfillmentCapacityConfirmed ────────

    [Fact]
    public async Task HandleAsync_InventoryReservationConfirmed_WhenCapacityNotYetConfirmed_NoShipmentCommand()
    {
        var orderId = await ProcessToConfirmingResourcesAsync();
        _outbox.Reset();

        await _sut.HandleAsync(
            new InventoryReservationConfirmedIntegrationEvent(Guid.NewGuid(), orderId),
            CancellationToken.None);

        Assert.Empty(_outbox.CallsFor("shipment.commands"));
        _db.ChangeTracker.Clear();
        var order = await _db.Orders.SingleAsync(x => x.Id == orderId);
        Assert.Equal(ReservationState.Confirmed, order.InventoryState);
        Assert.Equal(OrderStatus.ConfirmingResources, order.Status);
    }

    [Fact]
    public async Task HandleAsync_BothConfirmed_SendsCreateShipmentCommand()
    {
        var orderId = await ProcessToConfirmingResourcesAsync();
        await _sut.HandleAsync(new InventoryReservationConfirmedIntegrationEvent(Guid.NewGuid(), orderId), CancellationToken.None);
        _db.ChangeTracker.Clear();
        _outbox.Reset();

        await _sut.HandleAsync(
            new FulfillmentCapacityConfirmedIntegrationEvent(Guid.NewGuid(), orderId),
            CancellationToken.None);

        var cmd = _outbox.SingleMessage<CreateShipmentCommand>("shipment.commands");
        Assert.NotNull(cmd);
        Assert.Equal(orderId, cmd.OrderId);
        _db.ChangeTracker.Clear();
        var order = await _db.Orders.SingleAsync(x => x.Id == orderId);
        Assert.Equal(OrderStatus.CreatingShipment, order.Status);
    }

    // ─── ShipmentCreated ─────────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_ShipmentCreated_SendsCapturePaymentCommand()
    {
        var orderId = await ProcessToCreatingShipmentAsync();
        var shipmentId = Guid.NewGuid();
        _outbox.Reset();

        await _sut.HandleAsync(
            new ShipmentCreatedIntegrationEvent(Guid.NewGuid(), orderId, shipmentId),
            CancellationToken.None);

        var cmd = _outbox.SingleMessage<CapturePaymentCommand>("payment.commands");
        Assert.NotNull(cmd);
        Assert.Equal(orderId, cmd.OrderId);
        _db.ChangeTracker.Clear();
        var order = await _db.Orders.SingleAsync(x => x.Id == orderId);
        Assert.Equal(OrderStatus.AwaitingPaymentCapture, order.Status);
        Assert.Equal(shipmentId, order.ShipmentId);
    }

    // ─── ShipmentCreationFailed ──────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_ShipmentCreationFailed_CancelsAndCompensates()
    {
        var orderId = await ProcessToCreatingShipmentAsync();
        _outbox.Reset();

        await _sut.HandleAsync(
            new ShipmentCreationFailedIntegrationEvent(Guid.NewGuid(), orderId, "rota indisponível"),
            CancellationToken.None);

        _db.ChangeTracker.Clear();
        var order = await _db.Orders.SingleAsync(x => x.Id == orderId);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Single(_outbox.CallsFor("inventory.commands"));
        Assert.Single(_outbox.CallsFor("fulfillment.commands"));
        Assert.Single(_outbox.CallsFor("payment.commands"));
        Assert.Single(_outbox.CallsFor("order.events"));
    }

    // ─── PaymentCaptured ─────────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_PaymentCaptured_ConfirmsOrderAndPublishesEvent()
    {
        var orderId = await ProcessToAwaitingCaptureAsync();
        _outbox.Reset();

        await _sut.HandleAsync(
            new PaymentCapturedIntegrationEvent(Guid.NewGuid(), orderId),
            CancellationToken.None);

        _db.ChangeTracker.Clear();
        var order = await _db.Orders.SingleAsync(x => x.Id == orderId);
        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.NotNull(order.ConfirmedAt);
        var confirmed = _outbox.SingleMessage<OrderConfirmedIntegrationEvent>("order.events");
        Assert.NotNull(confirmed);
        Assert.Equal(orderId, confirmed.OrderId);
    }

    // ─── PaymentCaptureFailed ────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_PaymentCaptureFailed_TransitionsToPaymentReview()
    {
        var orderId = await ProcessToAwaitingCaptureAsync();
        _outbox.Reset();

        await _sut.HandleAsync(
            new PaymentCaptureFailedIntegrationEvent(Guid.NewGuid(), orderId, "recusado"),
            CancellationToken.None);

        _db.ChangeTracker.Clear();
        var order = await _db.Orders.SingleAsync(x => x.Id == orderId);
        Assert.Equal(OrderStatus.PaymentReview, order.Status);
        Assert.Empty(_outbox.Calls);
    }

    // ─── ShipmentStatusUpdated ───────────────────────────────────────────────

    [Fact]
    public async Task HandleShipmentStatusUpdatedAsync_UpdatesOrderShipmentStatus()
    {
        var orderId = await ProcessCheckoutAsync();
        var shipmentId = Guid.NewGuid();
        var statusDate = DateTimeOffset.UtcNow;
        var eventId = Guid.NewGuid();

        await _sut.HandleShipmentStatusUpdatedAsync(
            eventId,
            new ShipmentStatusUpdatedIntegrationEvent(
                shipmentId, orderId, Guid.NewGuid(),
                "BR123", "CORREIOS", null, "IN_TRANSIT", statusDate, null, null),
            CancellationToken.None);

        _db.ChangeTracker.Clear();
        var order = await _db.Orders.SingleAsync(x => x.Id == orderId);
        Assert.Equal("IN_TRANSIT", order.ShipmentStatus);
        Assert.Equal(statusDate, order.ShipmentStatusUpdatedAt);
        Assert.Equal(shipmentId, order.ShipmentId);
    }

    [Fact]
    public async Task HandleShipmentStatusUpdatedAsync_IdempotentOnDuplicate()
    {
        var orderId = await ProcessCheckoutAsync();
        var eventId = Guid.NewGuid();
        var evt = new ShipmentStatusUpdatedIntegrationEvent(
            Guid.NewGuid(), orderId, Guid.NewGuid(), "BR123", "CORREIOS", null, "IN_TRANSIT", DateTimeOffset.UtcNow, null, null);

        await _sut.HandleShipmentStatusUpdatedAsync(eventId, evt, CancellationToken.None);
        await _sut.HandleShipmentStatusUpdatedAsync(eventId, evt, CancellationToken.None);

        Assert.Equal(1, await _db.InboxMessages.CountAsync(x => x.MessageId == eventId));
    }

    // ─── Setup Helpers ────────────────────────────────────────────────────────

    private async Task<Guid> ProcessCheckoutAsync(CheckoutConfirmedIntegrationEvent? evt = null)
    {
        evt ??= CheckoutEvent();
        await _sut.HandleAsync(evt, CancellationToken.None);
        _db.ChangeTracker.Clear();
        return (await _db.Orders.SingleAsync(x => x.CheckoutId == evt.CheckoutId)).Id;
    }

    private async Task<Guid> ProcessBothReservedAsync()
    {
        var orderId = await ProcessCheckoutAsync();
        await _sut.HandleAsync(new InventoryReservedIntegrationEvent(Guid.NewGuid(), orderId, Guid.NewGuid()), CancellationToken.None);
        _db.ChangeTracker.Clear();
        await _sut.HandleAsync(new FulfillmentCapacityReservedIntegrationEvent(Guid.NewGuid(), orderId, Guid.NewGuid()), CancellationToken.None);
        _db.ChangeTracker.Clear();
        return orderId;
    }

    private async Task<Guid> ProcessToConfirmingResourcesAsync()
    {
        var orderId = await ProcessBothReservedAsync();
        await _sut.HandleAsync(new PaymentAuthorizedIntegrationEvent(Guid.NewGuid(), orderId, Guid.NewGuid()), CancellationToken.None);
        _db.ChangeTracker.Clear();
        return orderId;
    }

    private async Task<Guid> ProcessToCreatingShipmentAsync()
    {
        var orderId = await ProcessToConfirmingResourcesAsync();
        await _sut.HandleAsync(new InventoryReservationConfirmedIntegrationEvent(Guid.NewGuid(), orderId), CancellationToken.None);
        _db.ChangeTracker.Clear();
        await _sut.HandleAsync(new FulfillmentCapacityConfirmedIntegrationEvent(Guid.NewGuid(), orderId), CancellationToken.None);
        _db.ChangeTracker.Clear();
        return orderId;
    }

    private async Task<Guid> ProcessToAwaitingCaptureAsync()
    {
        var orderId = await ProcessToCreatingShipmentAsync();
        await _sut.HandleAsync(new ShipmentCreatedIntegrationEvent(Guid.NewGuid(), orderId, Guid.NewGuid()), CancellationToken.None);
        _db.ChangeTracker.Clear();
        return orderId;
    }

    private static CheckoutConfirmedIntegrationEvent CheckoutEvent(
        Guid? checkoutId = null,
        Guid? fulfillmentCenterId = null)
    {
        var sku = Guid.NewGuid();
        var fc = fulfillmentCenterId ?? Guid.NewGuid();

        return new CheckoutConfirmedIntegrationEvent(
            MessageId: Guid.NewGuid(),
            OccurredAt: DateTimeOffset.UtcNow,
            CheckoutId: checkoutId ?? Guid.NewGuid(),
            BuyerId: Guid.NewGuid(),
            SellerId: Guid.NewGuid(),
            Currency: "BRL",
            ShippingPrice: 15m,
            ShippingPromiseId: "promise-xyz",
            PricingQuoteId: Guid.NewGuid(),
            PaymentMethodToken: "tok_test",
            Items: [new CheckoutConfirmedItem(sku, "Produto Teste", 2, 50m, fc)],
            RouteId: "route-sp-1",
            CarrierCode: "CORREIOS",
            ServiceLevelCode: "PAC",
            OriginNodeId: fc,
            PromisedDeliveryDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)),
            Destination: new OrderDestinationDto("Rua A", "100", "São Paulo", "SP", "01310-100", "BR"),
            Packages:
            [
                new OrderPackageDto("pkg-1", 0.5m, 10m, 20m, 15m, [new OrderPackageItemDto(sku, 2)])
            ]);
    }

    // Simulates a Production environment so that missing shipment fields throw instead of returning mocks
    private sealed class ProductionEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "OrderService.Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
