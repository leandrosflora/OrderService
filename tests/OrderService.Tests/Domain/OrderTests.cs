using OrderService.Domain;
using Xunit;

namespace OrderService.Tests.Domain;

public sealed class OrderTests
{
    #region Create

    [Fact]
    public void Create_WithValidArguments_ReturnsOrderWithInitialState()
    {
        var checkoutId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var pricingId = Guid.NewGuid();
        var items = new[] { new OrderItem(Guid.NewGuid(), "SKU 1", 2, 30m) };

        var order = Order.Create(checkoutId, buyerId, sellerId, "BRL", 10m, "promise-1", pricingId, items);

        Assert.NotEqual(Guid.Empty, order.Id);
        Assert.Equal(checkoutId, order.CheckoutId);
        Assert.Equal(buyerId, order.BuyerId);
        Assert.Equal(sellerId, order.SellerId);
        Assert.Equal("BRL", order.Currency);
        Assert.Equal(10m, order.ShippingPrice);
        Assert.Equal(60m, order.ItemsTotal);
        Assert.Equal(70m, order.TotalAmount);
        Assert.Equal("promise-1", order.ShippingPromiseId);
        Assert.Equal(pricingId, order.PricingQuoteId);
        Assert.Equal(OrderStatus.ReservingResources, order.Status);
        Assert.Equal(ReservationState.Requested, order.InventoryState);
        Assert.Equal(ReservationState.Requested, order.CapacityState);
        Assert.Equal(PaymentState.NotRequested, order.PaymentState);
        Assert.Equal(ShipmentState.NotRequested, order.ShipmentState);
        Assert.Equal(1, order.Version);
        Assert.Single(order.Items);
        Assert.Null(order.CancellationReason);
    }

    [Fact]
    public void Create_TotalAmount_IsItemsTotalPlusShipping()
    {
        var items = new[]
        {
            new OrderItem(Guid.NewGuid(), "A", 2, 50m),
            new OrderItem(Guid.NewGuid(), "B", 1, 30m)
        };

        var order = Order.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "BRL", 20m, "p", Guid.NewGuid(), items);

        Assert.Equal(130m, order.ItemsTotal);
        Assert.Equal(150m, order.TotalAmount);
    }

    [Fact]
    public void Create_WithEmptyCheckoutId_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            Order.Create(Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), "BRL", 0m, "p", Guid.NewGuid(), [Item()]));
    }

    [Fact]
    public void Create_WithEmptyBuyerId_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            Order.Create(Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), "BRL", 0m, "p", Guid.NewGuid(), [Item()]));
    }

    [Fact]
    public void Create_WithEmptySellerId_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            Order.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, "BRL", 0m, "p", Guid.NewGuid(), [Item()]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_WithInvalidCurrency_Throws(string? currency)
    {
        Assert.Throws<ArgumentException>(() =>
            Order.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), currency!, 0m, "p", Guid.NewGuid(), [Item()]));
    }

    [Fact]
    public void Create_WithNegativeShippingPrice_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            Order.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "BRL", -1m, "p", Guid.NewGuid(), [Item()]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithInvalidShippingPromiseId_Throws(string? promiseId)
    {
        Assert.Throws<ArgumentException>(() =>
            Order.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "BRL", 0m, promiseId!, Guid.NewGuid(), [Item()]));
    }

    [Fact]
    public void Create_WithEmptyPricingQuoteId_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            Order.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "BRL", 0m, "p", Guid.Empty, [Item()]));
    }

    [Fact]
    public void Create_WithNoItems_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            Order.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "BRL", 0m, "p", Guid.NewGuid(), []));
    }

    #endregion

    #region MarkInventoryReserved

    [Fact]
    public void MarkInventoryReserved_WhenCapacityNotYetReserved_ReturnsFalse()
    {
        var order = NewOrder();

        var result = order.MarkInventoryReserved(Guid.NewGuid());

        Assert.False(result);
        Assert.Equal(ReservationState.Reserved, order.InventoryState);
        Assert.Equal(OrderStatus.ReservingResources, order.Status);
        Assert.Equal(PaymentState.NotRequested, order.PaymentState);
    }

    [Fact]
    public void MarkInventoryReserved_WhenAlreadyReserved_IsIdempotent()
    {
        var order = NewOrder();
        var firstId = Guid.NewGuid();
        order.MarkInventoryReserved(firstId);

        var result = order.MarkInventoryReserved(Guid.NewGuid());

        Assert.False(result);
        Assert.Equal(firstId, order.InventoryReservationId);
    }

    [Fact]
    public void MarkInventoryReserved_WithEmptyReservationId_Throws()
    {
        var order = NewOrder();

        Assert.Throws<ArgumentException>(() => order.MarkInventoryReserved(Guid.Empty));
    }

    [Fact]
    public void MarkInventoryReserved_WhenCancelled_Throws()
    {
        var order = NewOrder();
        order.Cancel("motivo");

        Assert.Throws<InvalidOperationException>(() => order.MarkInventoryReserved(Guid.NewGuid()));
    }

    #endregion

    #region MarkCapacityReserved

    [Fact]
    public void MarkCapacityReserved_WhenInventoryNotYetReserved_ReturnsFalse()
    {
        var order = NewOrder();

        var result = order.MarkCapacityReserved(Guid.NewGuid());

        Assert.False(result);
        Assert.Equal(ReservationState.Reserved, order.CapacityState);
        Assert.Equal(OrderStatus.ReservingResources, order.Status);
    }

    [Fact]
    public void MarkCapacityReserved_WhenAlreadyReserved_IsIdempotent()
    {
        var order = NewOrder();
        var firstId = Guid.NewGuid();
        order.MarkCapacityReserved(firstId);

        var result = order.MarkCapacityReserved(Guid.NewGuid());

        Assert.False(result);
        Assert.Equal(firstId, order.CapacityReservationId);
    }

    #endregion

    #region Both Reservations → Payment Auth

    [Fact]
    public void BothReserved_InventoryThenCapacity_StartsPaymentAuthorization()
    {
        var order = NewOrder();
        var invId = Guid.NewGuid();
        var capId = Guid.NewGuid();

        order.MarkInventoryReserved(invId);
        var result = order.MarkCapacityReserved(capId);

        Assert.True(result);
        Assert.Equal(OrderStatus.AwaitingPaymentAuthorization, order.Status);
        Assert.Equal(PaymentState.AuthorizationRequested, order.PaymentState);
        Assert.Equal(invId, order.InventoryReservationId);
        Assert.Equal(capId, order.CapacityReservationId);
    }

    [Fact]
    public void BothReserved_CapacityThenInventory_StartsPaymentAuthorization()
    {
        var order = NewOrder();

        order.MarkCapacityReserved(Guid.NewGuid());
        var result = order.MarkInventoryReserved(Guid.NewGuid());

        Assert.True(result);
        Assert.Equal(OrderStatus.AwaitingPaymentAuthorization, order.Status);
    }

    #endregion

    #region Reservation Failures

    [Fact]
    public void MarkInventoryReservationFailed_CancelsWithReason()
    {
        var order = NewOrder();

        order.MarkInventoryReservationFailed("sem estoque");

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(ReservationState.Failed, order.InventoryState);
        Assert.Contains("sem estoque", order.CancellationReason);
        Assert.NotNull(order.CancelledAt);
    }

    [Fact]
    public void MarkInventoryReservationFailed_WhenAlreadyCancelled_IsIdempotent()
    {
        var order = NewOrder();
        order.Cancel("primeiro motivo");

        order.MarkInventoryReservationFailed("outro motivo");

        Assert.Equal("primeiro motivo", order.CancellationReason);
    }

    [Fact]
    public void MarkCapacityReservationFailed_CancelsWithReason()
    {
        var order = NewOrder();

        order.MarkCapacityReservationFailed("sem capacidade");

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(ReservationState.Failed, order.CapacityState);
        Assert.Contains("sem capacidade", order.CancellationReason);
    }

    [Fact]
    public void MarkCapacityReservationFailed_WhenAlreadyCancelled_IsIdempotent()
    {
        var order = NewOrder();
        order.Cancel("primeiro motivo");

        order.MarkCapacityReservationFailed("outro motivo");

        Assert.Equal("primeiro motivo", order.CancellationReason);
    }

    #endregion

    #region MarkPaymentAuthorized

    [Fact]
    public void MarkPaymentAuthorized_WhenAwaitingAuthorization_TransitionsToConfirmingResources()
    {
        var order = OrderAwaitingPayment();
        var authId = Guid.NewGuid();

        order.MarkPaymentAuthorized(authId);

        Assert.Equal(OrderStatus.ConfirmingResources, order.Status);
        Assert.Equal(PaymentState.Authorized, order.PaymentState);
        Assert.Equal(authId, order.PaymentAuthorizationId);
    }

    [Fact]
    public void MarkPaymentAuthorized_WhenAlreadyAuthorized_IsIdempotent()
    {
        var order = OrderAwaitingPayment();
        var firstAuthId = Guid.NewGuid();
        order.MarkPaymentAuthorized(firstAuthId);

        order.MarkPaymentAuthorized(Guid.NewGuid());

        Assert.Equal(firstAuthId, order.PaymentAuthorizationId);
        Assert.Equal(PaymentState.Authorized, order.PaymentState);
    }

    [Fact]
    public void MarkPaymentAuthorized_WhenNotAwaitingAuthorization_Throws()
    {
        var order = NewOrder();

        Assert.Throws<InvalidOperationException>(() => order.MarkPaymentAuthorized(Guid.NewGuid()));
    }

    [Fact]
    public void MarkPaymentAuthorized_WithEmptyAuthorizationId_Throws()
    {
        var order = OrderAwaitingPayment();

        Assert.Throws<ArgumentException>(() => order.MarkPaymentAuthorized(Guid.Empty));
    }

    #endregion

    #region MarkPaymentAuthorizationFailed

    [Fact]
    public void MarkPaymentAuthorizationFailed_CancelsOrder()
    {
        var order = OrderAwaitingPayment();

        order.MarkPaymentAuthorizationFailed("saldo insuficiente");

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(PaymentState.Failed, order.PaymentState);
        Assert.Contains("saldo insuficiente", order.CancellationReason);
    }

    [Fact]
    public void MarkPaymentAuthorizationFailed_WhenAlreadyCancelled_IsIdempotent()
    {
        var order = OrderAwaitingPayment();
        order.Cancel("cancelado externamente");

        order.MarkPaymentAuthorizationFailed("fraude");

        Assert.Equal("cancelado externamente", order.CancellationReason);
    }

    [Fact]
    public void MarkPaymentAuthorizationFailed_WhenInWrongState_Throws()
    {
        var order = NewOrder();

        Assert.Throws<InvalidOperationException>(() => order.MarkPaymentAuthorizationFailed("reason"));
    }

    #endregion

    #region MarkInventoryConfirmed / MarkCapacityConfirmed

    [Fact]
    public void MarkInventoryConfirmed_WhenCapacityNotYetConfirmed_ReturnsFalse()
    {
        var order = OrderConfirmingResources();

        var result = order.MarkInventoryConfirmed();

        Assert.False(result);
        Assert.Equal(ReservationState.Confirmed, order.InventoryState);
        Assert.Equal(OrderStatus.ConfirmingResources, order.Status);
    }

    [Fact]
    public void MarkCapacityConfirmed_WhenInventoryNotYetConfirmed_ReturnsFalse()
    {
        var order = OrderConfirmingResources();

        var result = order.MarkCapacityConfirmed();

        Assert.False(result);
        Assert.Equal(ReservationState.Confirmed, order.CapacityState);
        Assert.Equal(OrderStatus.ConfirmingResources, order.Status);
    }

    [Fact]
    public void BothConfirmed_InventoryThenCapacity_StartsShipmentCreation()
    {
        var order = OrderConfirmingResources();

        order.MarkInventoryConfirmed();
        var result = order.MarkCapacityConfirmed();

        Assert.True(result);
        Assert.Equal(OrderStatus.CreatingShipment, order.Status);
        Assert.Equal(ShipmentState.Requested, order.ShipmentState);
    }

    [Fact]
    public void BothConfirmed_CapacityThenInventory_StartsShipmentCreation()
    {
        var order = OrderConfirmingResources();

        order.MarkCapacityConfirmed();
        var result = order.MarkInventoryConfirmed();

        Assert.True(result);
        Assert.Equal(OrderStatus.CreatingShipment, order.Status);
    }

    [Fact]
    public void MarkInventoryConfirmed_WhenAlreadyConfirmed_IsIdempotent()
    {
        var order = OrderConfirmingResources();
        order.MarkInventoryConfirmed();

        var result = order.MarkInventoryConfirmed();

        Assert.False(result);
    }

    [Fact]
    public void MarkInventoryConfirmed_WhenNotConfirmingResources_Throws()
    {
        var order = NewOrder();

        Assert.Throws<InvalidOperationException>(() => order.MarkInventoryConfirmed());
    }

    [Fact]
    public void MarkCapacityConfirmed_WhenNotConfirmingResources_Throws()
    {
        var order = NewOrder();

        Assert.Throws<InvalidOperationException>(() => order.MarkCapacityConfirmed());
    }

    #endregion

    #region MarkShipmentCreated

    [Fact]
    public void MarkShipmentCreated_WhenCreatingShipment_Transitions()
    {
        var order = OrderCreatingShipment();
        var shipmentId = Guid.NewGuid();

        order.MarkShipmentCreated(shipmentId);

        Assert.Equal(OrderStatus.AwaitingPaymentCapture, order.Status);
        Assert.Equal(ShipmentState.Created, order.ShipmentState);
        Assert.Equal(PaymentState.CaptureRequested, order.PaymentState);
        Assert.Equal(shipmentId, order.ShipmentId);
    }

    [Fact]
    public void MarkShipmentCreated_WhenAlreadyCreated_IsIdempotent()
    {
        var order = OrderCreatingShipment();
        var firstId = Guid.NewGuid();
        order.MarkShipmentCreated(firstId);

        order.MarkShipmentCreated(Guid.NewGuid());

        Assert.Equal(firstId, order.ShipmentId);
    }

    [Fact]
    public void MarkShipmentCreated_WithEmptyShipmentId_Throws()
    {
        var order = OrderCreatingShipment();

        Assert.Throws<ArgumentException>(() => order.MarkShipmentCreated(Guid.Empty));
    }

    [Fact]
    public void MarkShipmentCreated_WhenNotCreatingShipment_Throws()
    {
        var order = NewOrder();

        Assert.Throws<InvalidOperationException>(() => order.MarkShipmentCreated(Guid.NewGuid()));
    }

    #endregion

    #region MarkShipmentCreationFailed

    [Fact]
    public void MarkShipmentCreationFailed_WhenCreatingShipment_CancelsOrder()
    {
        var order = OrderCreatingShipment();

        order.MarkShipmentCreationFailed("transportadora indisponível");

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(ShipmentState.Failed, order.ShipmentState);
        Assert.Contains("transportadora indisponível", order.CancellationReason);
    }

    [Fact]
    public void MarkShipmentCreationFailed_WhenAlreadyCancelled_IsIdempotent()
    {
        var order = OrderCreatingShipment();
        order.MarkShipmentCreationFailed("primeiro motivo");

        order.MarkShipmentCreationFailed("segundo motivo");

        Assert.Contains("primeiro motivo", order.CancellationReason);
    }

    [Fact]
    public void MarkShipmentCreationFailed_WhenNotCreatingShipment_Throws()
    {
        var order = NewOrder();

        Assert.Throws<InvalidOperationException>(() => order.MarkShipmentCreationFailed("reason"));
    }

    #endregion

    #region MarkPaymentCaptured

    [Fact]
    public void MarkPaymentCaptured_WhenAwaitingCapture_TransitionsToConfirmed()
    {
        var order = OrderAwaitingCapture();

        order.MarkPaymentCaptured();

        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.Equal(PaymentState.Captured, order.PaymentState);
        Assert.NotNull(order.ConfirmedAt);
    }

    [Fact]
    public void MarkPaymentCaptured_WhenAlreadyCaptured_IsIdempotent()
    {
        var order = OrderAwaitingCapture();
        order.MarkPaymentCaptured();
        var confirmedAt = order.ConfirmedAt;

        order.MarkPaymentCaptured();

        Assert.Equal(confirmedAt, order.ConfirmedAt);
        Assert.Equal(PaymentState.Captured, order.PaymentState);
    }

    [Fact]
    public void MarkPaymentCaptured_WhenNotAwaitingCapture_Throws()
    {
        var order = NewOrder();

        Assert.Throws<InvalidOperationException>(() => order.MarkPaymentCaptured());
    }

    #endregion

    #region MarkPaymentCaptureFailed

    [Fact]
    public void MarkPaymentCaptureFailed_WhenAwaitingCapture_TransitionsToPaymentReview()
    {
        var order = OrderAwaitingCapture();

        order.MarkPaymentCaptureFailed();

        Assert.Equal(OrderStatus.PaymentReview, order.Status);
        Assert.Equal(PaymentState.Failed, order.PaymentState);
    }

    [Fact]
    public void MarkPaymentCaptureFailed_WhenNotAwaitingCapture_IsNoOp()
    {
        var order = NewOrder();

        order.MarkPaymentCaptureFailed();

        Assert.Equal(OrderStatus.ReservingResources, order.Status);
    }

    #endregion

    #region UpdateShipmentStatus

    [Fact]
    public void UpdateShipmentStatus_WithoutPriorShipmentId_SetsStatus()
    {
        var order = NewOrder();
        var shipmentId = Guid.NewGuid();
        var updatedAt = DateTimeOffset.UtcNow;

        order.UpdateShipmentStatus(shipmentId, "IN_TRANSIT", updatedAt);

        Assert.Equal(shipmentId, order.ShipmentId);
        Assert.Equal("IN_TRANSIT", order.ShipmentStatus);
        Assert.Equal(updatedAt, order.ShipmentStatusUpdatedAt);
    }

    [Fact]
    public void UpdateShipmentStatus_WithMatchingShipmentId_UpdatesStatus()
    {
        var order = NewOrder();
        var shipmentId = Guid.NewGuid();
        order.UpdateShipmentStatus(shipmentId, "LABEL_PRINTED", DateTimeOffset.UtcNow);

        order.UpdateShipmentStatus(shipmentId, "DELIVERED", DateTimeOffset.UtcNow.AddHours(1));

        Assert.Equal("DELIVERED", order.ShipmentStatus);
    }

    [Fact]
    public void UpdateShipmentStatus_WithDifferentShipmentId_IsIgnored()
    {
        var order = NewOrder();
        var correctId = Guid.NewGuid();
        order.UpdateShipmentStatus(correctId, "IN_TRANSIT", DateTimeOffset.UtcNow);

        order.UpdateShipmentStatus(Guid.NewGuid(), "DELIVERED", DateTimeOffset.UtcNow.AddHours(1));

        Assert.Equal("IN_TRANSIT", order.ShipmentStatus);
    }

    [Fact]
    public void UpdateShipmentStatus_WithEmptyShipmentId_Throws()
    {
        var order = NewOrder();

        Assert.Throws<ArgumentException>(() =>
            order.UpdateShipmentStatus(Guid.Empty, "IN_TRANSIT", DateTimeOffset.UtcNow));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void UpdateShipmentStatus_WithInvalidStatus_Throws(string? status)
    {
        var order = NewOrder();

        Assert.Throws<ArgumentException>(() =>
            order.UpdateShipmentStatus(Guid.NewGuid(), status!, DateTimeOffset.UtcNow));
    }

    #endregion

    #region Cancel

    [Fact]
    public void Cancel_WhenActive_CancelsOrder()
    {
        var order = NewOrder();

        order.Cancel("solicitado pelo comprador");

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal("solicitado pelo comprador", order.CancellationReason);
        Assert.NotNull(order.CancelledAt);
    }

    [Fact]
    public void Cancel_WhenAlreadyCancelled_IsIdempotent()
    {
        var order = NewOrder();
        order.Cancel("primeiro motivo");

        order.Cancel("outro motivo");

        Assert.Equal("primeiro motivo", order.CancellationReason);
    }

    [Fact]
    public void Cancel_WhenConfirmed_Throws()
    {
        var order = OrderConfirmed();

        Assert.Throws<InvalidOperationException>(() => order.Cancel("tentativa inválida"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Cancel_WithEmptyReason_Throws(string? reason)
    {
        var order = NewOrder();

        Assert.Throws<ArgumentException>(() => order.Cancel(reason!));
    }

    [Fact]
    public void Cancel_SetsVersionHigherThanCreation()
    {
        var order = NewOrder();
        var versionAtCreation = order.Version;

        order.Cancel("motivo");

        Assert.True(order.Version > versionAtCreation);
    }

    #endregion

    #region Helpers

    private static OrderItem Item() => new(Guid.NewGuid(), "Item", 1, 10m);

    private static Order NewOrder() =>
        Order.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "BRL", 10m, "promise-1", Guid.NewGuid(),
            [new OrderItem(Guid.NewGuid(), "Produto", 1, 100m)]);

    // Status: AwaitingPaymentAuthorization
    private static Order OrderAwaitingPayment()
    {
        var order = NewOrder();
        order.MarkInventoryReserved(Guid.NewGuid());
        order.MarkCapacityReserved(Guid.NewGuid());
        return order;
    }

    // Status: ConfirmingResources
    private static Order OrderConfirmingResources()
    {
        var order = OrderAwaitingPayment();
        order.MarkPaymentAuthorized(Guid.NewGuid());
        return order;
    }

    // Status: CreatingShipment
    private static Order OrderCreatingShipment()
    {
        var order = OrderConfirmingResources();
        order.MarkInventoryConfirmed();
        order.MarkCapacityConfirmed();
        return order;
    }

    // Status: AwaitingPaymentCapture
    private static Order OrderAwaitingCapture()
    {
        var order = OrderCreatingShipment();
        order.MarkShipmentCreated(Guid.NewGuid());
        return order;
    }

    // Status: Confirmed
    private static Order OrderConfirmed()
    {
        var order = OrderAwaitingCapture();
        order.MarkPaymentCaptured();
        return order;
    }

    #endregion
}
