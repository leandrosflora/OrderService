using OrderService.Domain;

namespace OrderService.Tests.Domain;

public sealed class OrderTests
{
    [Fact]
    public void Create_WithValidCheckoutData_StartsResourceReservationAndCalculatesTotals()
    {
        var item = new OrderItem(Guid.NewGuid(), "Produto", 2, 10.50m);

        var order = CreateOrder(items: [item], shippingPrice: 7.25m);

        Assert.NotEqual(Guid.Empty, order.Id);
        Assert.Equal(OrderStatus.ReservingResources, order.Status);
        Assert.Equal(ReservationState.Requested, order.InventoryState);
        Assert.Equal(ReservationState.Requested, order.CapacityState);
        Assert.Equal(PaymentState.NotRequested, order.PaymentState);
        Assert.Equal(ShipmentState.NotRequested, order.ShipmentState);
        Assert.Equal(21.00m, order.ItemsTotal);
        Assert.Equal(28.25m, order.TotalAmount);
        Assert.Single(order.Items);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, -0.01)]
    public void OrderItem_WithInvalidQuantityOrPrice_RejectsInvalidContractValues(int quantity, decimal unitPrice)
    {
        Assert.Throws<ArgumentException>(() => new OrderItem(Guid.NewGuid(), "Produto", quantity, unitPrice));
    }

    [Fact]
    public void MarkInventoryAndCapacityReserved_WhenBothReservationsSucceed_RequestsPaymentAuthorizationOnce()
    {
        var order = CreateOrder();

        var firstResult = order.MarkInventoryReserved(Guid.NewGuid());
        var secondResult = order.MarkCapacityReserved(Guid.NewGuid());
        var duplicateResult = order.MarkCapacityReserved(Guid.NewGuid());

        Assert.False(firstResult);
        Assert.True(secondResult);
        Assert.False(duplicateResult);
        Assert.Equal(OrderStatus.AwaitingPaymentAuthorization, order.Status);
        Assert.Equal(PaymentState.AuthorizationRequested, order.PaymentState);
    }

    [Fact]
    public void ConfirmReservations_AfterPaymentAuthorization_RequestsShipmentCreationOnce()
    {
        var order = CreateOrderAwaitingPaymentAuthorization();
        order.MarkPaymentAuthorized(Guid.NewGuid());

        var firstResult = order.MarkInventoryConfirmed();
        var secondResult = order.MarkCapacityConfirmed();
        var duplicateResult = order.MarkCapacityConfirmed();

        Assert.False(firstResult);
        Assert.True(secondResult);
        Assert.False(duplicateResult);
        Assert.Equal(OrderStatus.CreatingShipment, order.Status);
        Assert.Equal(ShipmentState.Requested, order.ShipmentState);
    }

    [Fact]
    public void MarkShipmentCreated_WhenOrderIsCreatingShipment_RequestsPaymentCapture()
    {
        var order = CreateOrderCreatingShipment();
        var shipmentId = Guid.NewGuid();

        order.MarkShipmentCreated(shipmentId);

        Assert.Equal(shipmentId, order.ShipmentId);
        Assert.Equal(ShipmentState.Created, order.ShipmentState);
        Assert.Equal(PaymentState.CaptureRequested, order.PaymentState);
        Assert.Equal(OrderStatus.AwaitingPaymentCapture, order.Status);
    }

    [Fact]
    public void MarkPaymentCaptured_WhenAwaitingCapture_ConfirmsOrder()
    {
        var order = CreateOrderAwaitingPaymentCapture();

        order.MarkPaymentCaptured();

        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.Equal(PaymentState.Captured, order.PaymentState);
        Assert.NotNull(order.ConfirmedAt);
    }

    [Fact]
    public void Cancel_WhenOrderAlreadyConfirmed_RequiresReturnOrRefundProcess()
    {
        var order = CreateOrderAwaitingPaymentCapture();
        order.MarkPaymentCaptured();

        var exception = Assert.Throws<InvalidOperationException>(() => order.Cancel("buyer requested"));

        Assert.Contains("return or refund", exception.Message);
    }

    [Fact]
    public void UpdateShipmentStatus_WithDifferentExistingShipment_IgnoresForeignShipmentEvent()
    {
        var order = CreateOrderAwaitingPaymentCapture();
        var originalShipmentId = order.ShipmentId;

        order.UpdateShipmentStatus(Guid.NewGuid(), "delivered", DateTimeOffset.UtcNow);

        Assert.Equal(originalShipmentId, order.ShipmentId);
        Assert.Null(order.ShipmentStatus);
    }

    private static Order CreateOrderAwaitingPaymentAuthorization()
    {
        var order = CreateOrder();
        order.MarkInventoryReserved(Guid.NewGuid());
        order.MarkCapacityReserved(Guid.NewGuid());
        return order;
    }

    private static Order CreateOrderCreatingShipment()
    {
        var order = CreateOrderAwaitingPaymentAuthorization();
        order.MarkPaymentAuthorized(Guid.NewGuid());
        order.MarkInventoryConfirmed();
        order.MarkCapacityConfirmed();
        return order;
    }

    private static Order CreateOrderAwaitingPaymentCapture()
    {
        var order = CreateOrderCreatingShipment();
        order.MarkShipmentCreated(Guid.NewGuid());
        return order;
    }

    private static Order CreateOrder(IEnumerable<OrderItem>? items = null, decimal shippingPrice = 5m)
    {
        return Order.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "BRL",
            shippingPrice,
            "promise-1",
            Guid.NewGuid(),
            items ?? [new OrderItem(Guid.NewGuid(), "Produto", 1, 10m)]);
    }
}
