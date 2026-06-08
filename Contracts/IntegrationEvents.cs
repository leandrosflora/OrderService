namespace OrderService.Contracts;

public sealed record CheckoutConfirmedIntegrationEvent(
    Guid MessageId,
    DateTimeOffset OccurredAt,
    Guid CheckoutId,
    Guid BuyerId,
    Guid SellerId,
    string Currency,
    decimal ShippingPrice,
    string ShippingPromiseId,
    Guid PricingQuoteId,
    string PaymentMethodToken,
    IReadOnlyList<CheckoutConfirmedItem> Items);

public sealed record CheckoutConfirmedItem(
    Guid SkuId,
    string Title,
    int Quantity,
    decimal UnitPrice,
    Guid FulfillmentCenterId);

public sealed record InventoryReservedIntegrationEvent(
    Guid MessageId,
    Guid OrderId,
    Guid ReservationId);

public sealed record InventoryReservationFailedIntegrationEvent(
    Guid MessageId,
    Guid OrderId,
    string Reason);

public sealed record FulfillmentCapacityReservedIntegrationEvent(
    Guid MessageId,
    Guid OrderId,
    Guid ReservationId);

public sealed record FulfillmentCapacityReservationFailedIntegrationEvent(
    Guid MessageId,
    Guid OrderId,
    string Reason);

public sealed record PaymentAuthorizedIntegrationEvent(
    Guid MessageId,
    Guid OrderId,
    Guid PaymentAuthorizationId);

public sealed record PaymentAuthorizationFailedIntegrationEvent(
    Guid MessageId,
    Guid OrderId,
    string Reason);

public sealed record InventoryReservationConfirmedIntegrationEvent(
    Guid MessageId,
    Guid OrderId);

public sealed record FulfillmentCapacityConfirmedIntegrationEvent(
    Guid MessageId,
    Guid OrderId);

public sealed record ShipmentCreatedIntegrationEvent(
    Guid MessageId,
    Guid OrderId,
    Guid ShipmentId);

public sealed record ShipmentCreationFailedIntegrationEvent(
    Guid MessageId,
    Guid OrderId,
    string Reason);

public sealed record PaymentCapturedIntegrationEvent(
    Guid MessageId,
    Guid OrderId);

public sealed record PaymentCaptureFailedIntegrationEvent(
    Guid MessageId,
    Guid OrderId,
    string Reason);

public sealed record OrderConfirmedIntegrationEvent(
    Guid MessageId,
    Guid OrderId,
    Guid CheckoutId,
    Guid BuyerId,
    Guid SellerId,
    decimal TotalAmount,
    string Currency,
    Guid? ShipmentId,
    DateTimeOffset? ConfirmedAt);

public sealed record OrderCancelledIntegrationEvent(
    Guid MessageId,
    Guid OrderId,
    string Status,
    string? CancellationReason);
