namespace OrderService.Contracts;

public sealed record OrderItemResponse(
    Guid SkuId,
    string Title,
    int Quantity,
    decimal UnitPrice,
    decimal TotalPrice);

public sealed record OrderResponse(
    Guid Id,
    Guid CheckoutId,
    Guid BuyerId,
    Guid SellerId,
    string Status,
    string Currency,
    decimal ItemsTotal,
    decimal ShippingPrice,
    decimal TotalAmount,
    string ShippingPromiseId,
    Guid PricingQuoteId,
    Guid? InventoryReservationId,
    Guid? CapacityReservationId,
    Guid? PaymentAuthorizationId,
    Guid? ShipmentId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ConfirmedAt,
    DateTimeOffset? CancelledAt,
    IReadOnlyList<OrderItemResponse> Items);

public sealed record CancelOrderRequest(string Reason);
