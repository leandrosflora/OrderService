namespace OrderService.Contracts;

public sealed record ReserveInventoryCommand(
    Guid MessageId,
    Guid OrderId,
    Guid SellerId,
    IReadOnlyList<InventoryReservationItem> Items);

public sealed record InventoryReservationItem(
    Guid SkuId,
    Guid FulfillmentCenterId,
    int Quantity);

public sealed record ReserveFulfillmentCapacityCommand(
    Guid MessageId,
    Guid OrderId,
    Guid FulfillmentCenterId,
    int CapacityUnits);

public sealed record AuthorizePaymentCommand(
    Guid MessageId,
    Guid OrderId,
    Guid BuyerId,
    decimal Amount,
    string Currency,
    string PaymentMethodToken);

public sealed record ConfirmInventoryReservationCommand(
    Guid MessageId,
    Guid OrderId,
    Guid ReservationId);

public sealed record ConfirmFulfillmentCapacityCommand(
    Guid MessageId,
    Guid OrderId,
    Guid ReservationId);

public sealed record CreateShipmentCommand(
    Guid MessageId,
    Guid OrderId,
    string ShippingPromiseId,
    Guid InventoryReservationId,
    Guid CapacityReservationId);

public sealed record CapturePaymentCommand(
    Guid MessageId,
    Guid OrderId,
    Guid PaymentAuthorizationId,
    decimal Amount,
    string Currency);

public sealed record ReleaseInventoryReservationCommand(
    Guid MessageId,
    Guid OrderId,
    Guid ReservationId);

public sealed record ReleaseFulfillmentCapacityCommand(
    Guid MessageId,
    Guid OrderId,
    Guid ReservationId);

public sealed record VoidPaymentAuthorizationCommand(
    Guid MessageId,
    Guid OrderId,
    Guid PaymentAuthorizationId);
