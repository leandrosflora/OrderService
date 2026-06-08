using Microsoft.EntityFrameworkCore;
using OrderService.Domain;
using OrderService.Infrastructure.Outbox;

namespace OrderService.Infrastructure.Persistence;

public sealed class OrderDbContext : DbContext
{
    public OrderDbContext(DbContextOptions<OrderDbContext> options)
        : base(options)
    {
    }

    public DbSet<Order> Orders => Set<Order>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("orders");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.CheckoutId).IsUnique();
            entity.HasIndex(x => new { x.BuyerId, x.CreatedAt });
            entity.HasIndex(x => new { x.SellerId, x.CreatedAt });
            entity.HasIndex(x => new { x.Status, x.UpdatedAt });

            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.CheckoutId).HasColumnName("checkout_id");
            entity.Property(x => x.BuyerId).HasColumnName("buyer_id");
            entity.Property(x => x.SellerId).HasColumnName("seller_id");
            entity.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(50);
            entity.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
            entity.Property(x => x.ItemsTotal).HasColumnName("items_total").HasPrecision(18, 2);
            entity.Property(x => x.ShippingPrice).HasColumnName("shipping_price").HasPrecision(18, 2);
            entity.Property(x => x.TotalAmount).HasColumnName("total_amount").HasPrecision(18, 2);
            entity.Property(x => x.ShippingPromiseId).HasColumnName("shipping_promise_id").HasMaxLength(200).IsRequired();
            entity.Property(x => x.PricingQuoteId).HasColumnName("pricing_quote_id");
            entity.Property(x => x.InventoryReservationId).HasColumnName("inventory_reservation_id");
            entity.Property(x => x.CapacityReservationId).HasColumnName("capacity_reservation_id");
            entity.Property(x => x.PaymentAuthorizationId).HasColumnName("payment_authorization_id");
            entity.Property(x => x.ShipmentId).HasColumnName("shipment_id");
            entity.Property(x => x.InventoryState).HasColumnName("inventory_state").HasConversion<string>().HasMaxLength(30);
            entity.Property(x => x.CapacityState).HasColumnName("capacity_state").HasConversion<string>().HasMaxLength(30);
            entity.Property(x => x.PaymentState).HasColumnName("payment_state").HasConversion<string>().HasMaxLength(30);
            entity.Property(x => x.ShipmentState).HasColumnName("shipment_state").HasConversion<string>().HasMaxLength(30);
            entity.Property(x => x.CancellationReason).HasColumnName("cancellation_reason").HasMaxLength(500);
            entity.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            entity.Property(x => x.ConfirmedAt).HasColumnName("confirmed_at");
            entity.Property(x => x.CancelledAt).HasColumnName("cancelled_at");

            entity.OwnsMany(x => x.Items, item =>
            {
                item.ToTable("order_items");
                item.WithOwner().HasForeignKey("order_id");
                item.HasKey(x => x.Id);
                item.Property(x => x.Id).HasColumnName("id");
                item.Property<Guid>("order_id").HasColumnName("order_id");
                item.Property(x => x.SkuId).HasColumnName("sku_id");
                item.Property(x => x.Title).HasColumnName("title").HasMaxLength(300).IsRequired();
                item.Property(x => x.Quantity).HasColumnName("quantity");
                item.Property(x => x.UnitPrice).HasColumnName("unit_price").HasPrecision(18, 2);
            });

            entity.Navigation(x => x.Items).UsePropertyAccessMode(PropertyAccessMode.Property);
        });

        modelBuilder.Entity<InboxMessage>(entity =>
        {
            entity.ToTable("inbox_messages");
            entity.HasKey(x => x.MessageId);
            entity.Property(x => x.MessageId).HasColumnName("message_id");
            entity.Property(x => x.MessageType).HasColumnName("message_type").HasMaxLength(200).IsRequired();
            entity.Property(x => x.ProcessedAt).HasColumnName("processed_at");
        });

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("outbox_messages");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.Topic).HasColumnName("topic").HasMaxLength(200).IsRequired();
            entity.Property(x => x.MessageType).HasColumnName("message_type").HasMaxLength(200).IsRequired();
            entity.Property(x => x.AggregateKey).HasColumnName("aggregate_key").HasMaxLength(100).IsRequired();
            entity.Property(x => x.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.ProcessedAt).HasColumnName("processed_at");
            entity.Property(x => x.Attempts).HasColumnName("attempts");
            entity.Property(x => x.NextAttemptAt).HasColumnName("next_attempt_at");
            entity.Property(x => x.LastError).HasColumnName("last_error");
            entity.HasIndex(x => new { x.ProcessedAt, x.NextAttemptAt, x.CreatedAt });
        });
    }
}
