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
    IReadOnlyList<CheckoutConfirmedItem> Items,
    string? RouteId = null,
    string? CarrierCode = null,
    string? ServiceLevelCode = null,
    Guid? OriginNodeId = null,
    DateOnly? PromisedDeliveryDate = null,
    OrderDestinationDto? Destination = null,
    IReadOnlyList<OrderPackageDto>? Packages = null);

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

public sealed record OrderDestinationDto(
    string Street,
    string Number,
    string City,
    string State,
    string ZipCode,
    string Country);

public sealed record OrderPackageDto(
    string PackageId,
    decimal WeightKg,
    decimal HeightCm,
    decimal WidthCm,
    decimal LengthCm,
    IReadOnlyList<OrderPackageItemDto> Items);

public sealed record OrderPackageItemDto(
    Guid SkuId,
    int Quantity);

public sealed record OrderCreatedIntegrationEvent(
    Guid MessageId,
    Guid OrderId,
    Guid CheckoutId,
    Guid BuyerId,
    Guid SellerId,
    string ShippingPromiseId,
    string RouteId,
    string CarrierCode,
    string ServiceLevelCode,
    Guid OriginNodeId,
    DateOnly PromisedDeliveryDate,
    OrderDestinationDto Destination,
    IReadOnlyList<OrderPackageDto> Packages,
    decimal TotalAmount,
    string Currency,
    DateTimeOffset CreatedAt);

public sealed record ShipmentStatusUpdatedIntegrationEvent(
    Guid ShipmentId,
    Guid OrderId,
    Guid BuyerId,
    string TrackingCode,
    string CarrierCode,
    string? PreviousStatus,
    string CurrentStatus,
    DateTimeOffset StatusDate,
    DateOnly? EstimatedDeliveryDate,
    string? ExceptionCode);
