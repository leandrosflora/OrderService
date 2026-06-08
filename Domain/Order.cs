namespace OrderService.Domain;

public sealed class Order
{
    public Guid Id { get; private set; }
    public Guid CheckoutId { get; private set; }
    public Guid BuyerId { get; private set; }
    public Guid SellerId { get; private set; }
    public OrderStatus Status { get; private set; }
    public string Currency { get; private set; } = default!;
    public decimal ItemsTotal { get; private set; }
    public decimal ShippingPrice { get; private set; }
    public decimal TotalAmount { get; private set; }
    public string ShippingPromiseId { get; private set; } = default!;
    public Guid PricingQuoteId { get; private set; }
    public Guid? InventoryReservationId { get; private set; }
    public Guid? CapacityReservationId { get; private set; }
    public Guid? PaymentAuthorizationId { get; private set; }
    public Guid? ShipmentId { get; private set; }
    public ReservationState InventoryState { get; private set; }
    public ReservationState CapacityState { get; private set; }
    public PaymentState PaymentState { get; private set; }
    public ShipmentState ShipmentState { get; private set; }
    public string? CancellationReason { get; private set; }
    public long Version { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? ConfirmedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public List<OrderItem> Items { get; private set; } = [];

    private Order()
    {
    }

    public static Order Create(
        Guid checkoutId,
        Guid buyerId,
        Guid sellerId,
        string currency,
        decimal shippingPrice,
        string shippingPromiseId,
        Guid pricingQuoteId,
        IEnumerable<OrderItem> items)
    {
        var itemList = items.ToList();

        if (checkoutId == Guid.Empty)
        {
            throw new ArgumentException("CheckoutId is required", nameof(checkoutId));
        }

        if (buyerId == Guid.Empty)
        {
            throw new ArgumentException("BuyerId is required", nameof(buyerId));
        }

        if (sellerId == Guid.Empty)
        {
            throw new ArgumentException("SellerId is required", nameof(sellerId));
        }

        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new ArgumentException("Currency is required", nameof(currency));
        }

        if (shippingPrice < 0)
        {
            throw new ArgumentException("ShippingPrice cannot be negative", nameof(shippingPrice));
        }

        if (string.IsNullOrWhiteSpace(shippingPromiseId))
        {
            throw new ArgumentException("ShippingPromiseId is required", nameof(shippingPromiseId));
        }

        if (pricingQuoteId == Guid.Empty)
        {
            throw new ArgumentException("PricingQuoteId is required", nameof(pricingQuoteId));
        }

        if (itemList.Count == 0)
        {
            throw new ArgumentException("Order must have items", nameof(items));
        }

        var now = DateTimeOffset.UtcNow;
        var itemsTotal = itemList.Sum(x => x.TotalPrice);

        return new Order
        {
            Id = Guid.NewGuid(),
            CheckoutId = checkoutId,
            BuyerId = buyerId,
            SellerId = sellerId,
            Currency = currency,
            ItemsTotal = itemsTotal,
            ShippingPrice = shippingPrice,
            TotalAmount = itemsTotal + shippingPrice,
            ShippingPromiseId = shippingPromiseId,
            PricingQuoteId = pricingQuoteId,
            Status = OrderStatus.ReservingResources,
            InventoryState = ReservationState.Requested,
            CapacityState = ReservationState.Requested,
            PaymentState = PaymentState.NotRequested,
            ShipmentState = ShipmentState.NotRequested,
            Items = itemList,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public bool MarkInventoryReserved(Guid reservationId)
    {
        if (InventoryState is ReservationState.Reserved or ReservationState.Confirmed)
        {
            return false;
        }

        EnsureNotTerminal();
        EnsureReservationId(reservationId);

        InventoryReservationId = reservationId;
        InventoryState = ReservationState.Reserved;
        Touch();

        return TryStartPaymentAuthorization();
    }

    public bool MarkCapacityReserved(Guid reservationId)
    {
        if (CapacityState is ReservationState.Reserved or ReservationState.Confirmed)
        {
            return false;
        }

        EnsureNotTerminal();
        EnsureReservationId(reservationId);

        CapacityReservationId = reservationId;
        CapacityState = ReservationState.Reserved;
        Touch();

        return TryStartPaymentAuthorization();
    }

    public void MarkInventoryReservationFailed(string reason)
    {
        if (Status == OrderStatus.Cancelled)
        {
            return;
        }

        EnsureNotTerminal();
        InventoryState = ReservationState.Failed;
        Cancel($"Inventory reservation failed: {reason}");
    }

    public void MarkCapacityReservationFailed(string reason)
    {
        if (Status == OrderStatus.Cancelled)
        {
            return;
        }

        EnsureNotTerminal();
        CapacityState = ReservationState.Failed;
        Cancel($"Fulfillment capacity reservation failed: {reason}");
    }

    public void MarkPaymentAuthorized(Guid authorizationId)
    {
        if (PaymentState == PaymentState.Authorized)
        {
            return;
        }

        if (Status != OrderStatus.AwaitingPaymentAuthorization)
        {
            throw new InvalidOperationException("Order is not awaiting payment authorization");
        }

        if (authorizationId == Guid.Empty)
        {
            throw new ArgumentException("Payment authorization id is required", nameof(authorizationId));
        }

        PaymentAuthorizationId = authorizationId;
        PaymentState = PaymentState.Authorized;
        Status = OrderStatus.ConfirmingResources;
        Touch();
    }

    public void MarkPaymentAuthorizationFailed(string reason)
    {
        if (Status == OrderStatus.Cancelled)
        {
            return;
        }

        if (Status != OrderStatus.AwaitingPaymentAuthorization)
        {
            throw new InvalidOperationException("Order is not awaiting payment authorization");
        }

        PaymentState = PaymentState.Failed;
        Cancel($"Payment authorization failed: {reason}");
    }

    public bool MarkInventoryConfirmed()
    {
        if (InventoryState == ReservationState.Confirmed)
        {
            return false;
        }

        if (Status != OrderStatus.ConfirmingResources)
        {
            throw new InvalidOperationException("Order is not confirming resources");
        }

        InventoryState = ReservationState.Confirmed;
        Touch();

        return TryStartShipmentCreation();
    }

    public bool MarkCapacityConfirmed()
    {
        if (CapacityState == ReservationState.Confirmed)
        {
            return false;
        }

        if (Status != OrderStatus.ConfirmingResources)
        {
            throw new InvalidOperationException("Order is not confirming resources");
        }

        CapacityState = ReservationState.Confirmed;
        Touch();

        return TryStartShipmentCreation();
    }

    public void MarkShipmentCreated(Guid shipmentId)
    {
        if (ShipmentState == ShipmentState.Created)
        {
            return;
        }

        if (Status != OrderStatus.CreatingShipment)
        {
            throw new InvalidOperationException("Order is not creating shipment");
        }

        if (shipmentId == Guid.Empty)
        {
            throw new ArgumentException("ShipmentId is required", nameof(shipmentId));
        }

        ShipmentId = shipmentId;
        ShipmentState = ShipmentState.Created;
        PaymentState = PaymentState.CaptureRequested;
        Status = OrderStatus.AwaitingPaymentCapture;
        Touch();
    }

    public void MarkShipmentCreationFailed(string reason)
    {
        if (Status == OrderStatus.Cancelled)
        {
            return;
        }

        if (Status != OrderStatus.CreatingShipment)
        {
            throw new InvalidOperationException("Order is not creating shipment");
        }

        ShipmentState = ShipmentState.Failed;
        Cancel($"Shipment creation failed: {reason}");
    }

    public void MarkPaymentCaptured()
    {
        if (PaymentState == PaymentState.Captured)
        {
            return;
        }

        if (Status != OrderStatus.AwaitingPaymentCapture)
        {
            throw new InvalidOperationException("Order is not awaiting payment capture");
        }

        PaymentState = PaymentState.Captured;
        Status = OrderStatus.Confirmed;
        ConfirmedAt = DateTimeOffset.UtcNow;
        Touch();
    }

    public void MarkPaymentCaptureFailed()
    {
        if (Status != OrderStatus.AwaitingPaymentCapture)
        {
            return;
        }

        PaymentState = PaymentState.Failed;
        Status = OrderStatus.PaymentReview;
        Touch();
    }

    public void Cancel(string reason)
    {
        if (Status == OrderStatus.Cancelled)
        {
            return;
        }

        if (Status == OrderStatus.Confirmed)
        {
            throw new InvalidOperationException("Confirmed order requires a return or refund process");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Cancellation reason is required", nameof(reason));
        }

        Status = OrderStatus.Cancelled;
        CancellationReason = reason;
        CancelledAt = DateTimeOffset.UtcNow;
        Touch();
    }

    private bool TryStartPaymentAuthorization()
    {
        if (InventoryState != ReservationState.Reserved || CapacityState != ReservationState.Reserved)
        {
            return false;
        }

        if (PaymentState != PaymentState.NotRequested)
        {
            return false;
        }

        PaymentState = PaymentState.AuthorizationRequested;
        Status = OrderStatus.AwaitingPaymentAuthorization;
        Touch();

        return true;
    }

    private bool TryStartShipmentCreation()
    {
        if (InventoryState != ReservationState.Confirmed || CapacityState != ReservationState.Confirmed)
        {
            return false;
        }

        if (ShipmentState != ShipmentState.NotRequested)
        {
            return false;
        }

        ShipmentState = ShipmentState.Requested;
        Status = OrderStatus.CreatingShipment;
        Touch();

        return true;
    }

    private void EnsureNotTerminal()
    {
        if (Status is OrderStatus.Cancelled or OrderStatus.Confirmed or OrderStatus.Failed)
        {
            throw new InvalidOperationException("Order is already in a terminal state");
        }
    }

    private static void EnsureReservationId(Guid reservationId)
    {
        if (reservationId == Guid.Empty)
        {
            throw new ArgumentException("ReservationId is required", nameof(reservationId));
        }
    }

    private void Touch()
    {
        Version++;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
