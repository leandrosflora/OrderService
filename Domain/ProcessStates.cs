namespace OrderService.Domain;

public enum ReservationState
{
    NotRequested = 1,
    Requested = 2,
    Reserved = 3,
    Confirmed = 4,
    Failed = 5,
    Released = 6
}

public enum PaymentState
{
    NotRequested = 1,
    AuthorizationRequested = 2,
    Authorized = 3,
    CaptureRequested = 4,
    Captured = 5,
    Failed = 6,
    Voided = 7
}

public enum ShipmentState
{
    NotRequested = 1,
    Requested = 2,
    Created = 3,
    Failed = 4,
    Cancelled = 5
}
