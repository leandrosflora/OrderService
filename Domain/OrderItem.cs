namespace OrderService.Domain;

public sealed class OrderItem
{
    public Guid Id { get; private set; }
    public Guid SkuId { get; private set; }
    public string Title { get; private set; } = default!;
    public int Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal TotalPrice => Quantity * UnitPrice;

    private OrderItem()
    {
    }

    public OrderItem(Guid skuId, string title, int quantity, decimal unitPrice)
    {
        if (skuId == Guid.Empty)
        {
            throw new ArgumentException("SkuId is required", nameof(skuId));
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Title is required", nameof(title));
        }

        if (quantity <= 0)
        {
            throw new ArgumentException("Quantity must be positive", nameof(quantity));
        }

        if (unitPrice < 0)
        {
            throw new ArgumentException("UnitPrice cannot be negative", nameof(unitPrice));
        }

        Id = Guid.NewGuid();
        SkuId = skuId;
        Title = title;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }
}
