namespace OrderService.Domain;

public enum OrderStatus
{
    ReservingResources = 1,
    AwaitingPaymentAuthorization = 2,
    ConfirmingResources = 3,
    CreatingShipment = 4,
    AwaitingPaymentCapture = 5,
    Confirmed = 6,
    Cancelled = 7,
    PaymentReview = 8,
    Failed = 9
}
