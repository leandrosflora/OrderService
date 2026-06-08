CREATE TABLE orders (
    id UUID PRIMARY KEY,
    checkout_id UUID NOT NULL UNIQUE,
    buyer_id UUID NOT NULL,
    seller_id UUID NOT NULL,
    status VARCHAR(50) NOT NULL,
    currency VARCHAR(3) NOT NULL,
    items_total NUMERIC(18,2) NOT NULL,
    shipping_price NUMERIC(18,2) NOT NULL,
    total_amount NUMERIC(18,2) NOT NULL,
    shipping_promise_id VARCHAR(200) NOT NULL,
    pricing_quote_id UUID NOT NULL,
    inventory_reservation_id UUID NULL,
    capacity_reservation_id UUID NULL,
    payment_authorization_id UUID NULL,
    shipment_id UUID NULL,
    inventory_state VARCHAR(30) NOT NULL,
    capacity_state VARCHAR(30) NOT NULL,
    payment_state VARCHAR(30) NOT NULL,
    shipment_state VARCHAR(30) NOT NULL,
    cancellation_reason VARCHAR(500) NULL,
    version BIGINT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL,
    confirmed_at TIMESTAMPTZ NULL,
    cancelled_at TIMESTAMPTZ NULL
);

CREATE INDEX idx_orders_buyer_created
ON orders (buyer_id, created_at DESC);

CREATE INDEX idx_orders_seller_created
ON orders (seller_id, created_at DESC);

CREATE INDEX idx_orders_status_updated
ON orders (status, updated_at);

CREATE TABLE order_items (
    id UUID PRIMARY KEY,
    order_id UUID NOT NULL,
    sku_id UUID NOT NULL,
    title VARCHAR(300) NOT NULL,
    quantity INTEGER NOT NULL,
    unit_price NUMERIC(18,2) NOT NULL,
    FOREIGN KEY (order_id) REFERENCES orders(id)
);

CREATE TABLE inbox_messages (
    message_id UUID PRIMARY KEY,
    message_type VARCHAR(200) NOT NULL,
    processed_at TIMESTAMPTZ NOT NULL
);

CREATE TABLE outbox_messages (
    id UUID PRIMARY KEY,
    topic VARCHAR(200) NOT NULL,
    message_type VARCHAR(200) NOT NULL,
    aggregate_key VARCHAR(100) NOT NULL,
    payload JSONB NOT NULL,
    created_at TIMESTAMPTZ NOT NULL,
    processed_at TIMESTAMPTZ NULL,
    attempts INTEGER NOT NULL DEFAULT 0,
    next_attempt_at TIMESTAMPTZ NULL,
    last_error TEXT NULL
);

CREATE INDEX idx_outbox_pending
ON outbox_messages (processed_at, next_attempt_at, created_at);
