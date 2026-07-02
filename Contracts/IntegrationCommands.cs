using System.Text.Json.Serialization;

namespace OrderService.Contracts;

public sealed record ReserveInventoryCommand(
    Guid MessageId,
    Guid OrderId,
    Guid SellerId,
    IReadOnlyList<InventoryReservationItem> Items)
{
    // InventoryCommandsConsumer routes on this field; it must match one of the
    // case labels in its switch statement.
    [property: JsonPropertyName("commandType")]
    public string CommandType { get; init; } = "ReserveInventory";
}

public sealed record InventoryReservationItem(
    Guid SkuId,
    Guid FulfillmentCenterId,
    int Quantity);

public sealed record ReserveFulfillmentCapacityCommand(
    Guid MessageId,
    Guid OrderId,
    Guid FulfillmentCenterId,
    int CapacityUnits)
{
    // FulfillmentCommandsConsumer routes on this field; it must match one of the
    // case labels in its switch statement.
    [property: JsonPropertyName("commandType")]
    public string CommandType { get; init; } = "ReserveFulfillmentCapacity";
}

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
    Guid ReservationId)
{
    [property: JsonPropertyName("commandType")]
    public string CommandType { get; init; } = "ConfirmInventoryReservation";
}

public sealed record ConfirmFulfillmentCapacityCommand(
    Guid MessageId,
    Guid OrderId,
    Guid ReservationId)
{
    [property: JsonPropertyName("commandType")]
    public string CommandType { get; init; } = "ConfirmFulfillmentCapacity";
}

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
    Guid ReservationId)
{
    [property: JsonPropertyName("commandType")]
    public string CommandType { get; init; } = "ReleaseInventoryReservation";
}

public sealed record ReleaseFulfillmentCapacityCommand(
    Guid MessageId,
    Guid OrderId,
    Guid ReservationId)
{
    [property: JsonPropertyName("commandType")]
    public string CommandType { get; init; } = "ReleaseFulfillmentCapacity";
}

public sealed record VoidPaymentAuthorizationCommand(
    Guid MessageId,
    Guid OrderId,
    Guid PaymentAuthorizationId);
