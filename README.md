# Order Service

Order Service owns the commercial order and orchestrates the order lifecycle with a Saga based on local transactions, an idempotent Inbox, and a transactional Outbox.

## Main flow

1. Consume `CheckoutConfirmedIntegrationEvent`.
2. Persist an `Order` snapshot with item prices, shipping price, quote, and selected promise.
3. Publish commands to reserve inventory and fulfillment capacity.
4. Authorize payment after both reservations succeed.
5. Confirm inventory and capacity reservations after authorization succeeds.
6. Create shipment after both resources are confirmed.
7. Capture payment after shipment creation succeeds.
8. Publish `OrderConfirmedIntegrationEvent`.

Failures before the payment-capture pivot publish compensating commands, such as releasing reservations and voiding payment authorization.
