using OrderService.Domain;
using Xunit;

namespace OrderService.Tests.Domain;

public sealed class OrderItemTests
{
    [Fact]
    public void Constructor_WithValidArguments_SetsProperties()
    {
        var skuId = Guid.NewGuid();

        var item = new OrderItem(skuId, "Produto Teste", 3, 25.50m);

        Assert.Equal(skuId, item.SkuId);
        Assert.Equal("Produto Teste", item.Title);
        Assert.Equal(3, item.Quantity);
        Assert.Equal(25.50m, item.UnitPrice);
        Assert.NotEqual(Guid.Empty, item.Id);
    }

    [Fact]
    public void TotalPrice_EqualsQuantityTimesUnitPrice()
    {
        var item = new OrderItem(Guid.NewGuid(), "Item", 4, 12.50m);

        Assert.Equal(50.00m, item.TotalPrice);
    }

    [Fact]
    public void TotalPrice_WithZeroUnitPrice_IsZero()
    {
        var item = new OrderItem(Guid.NewGuid(), "Brinde", 1, 0m);

        Assert.Equal(0m, item.TotalPrice);
    }

    [Fact]
    public void Constructor_WithEmptySkuId_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            new OrderItem(Guid.Empty, "Title", 1, 10m));

        Assert.Equal("skuId", ex.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_WithInvalidTitle_ThrowsArgumentException(string? title)
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            new OrderItem(Guid.NewGuid(), title!, 1, 10m));

        Assert.Equal("title", ex.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Constructor_WithNonPositiveQuantity_ThrowsArgumentException(int quantity)
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            new OrderItem(Guid.NewGuid(), "Title", quantity, 10m));

        Assert.Equal("quantity", ex.ParamName);
    }

    [Fact]
    public void Constructor_WithNegativeUnitPrice_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            new OrderItem(Guid.NewGuid(), "Title", 1, -0.01m));

        Assert.Equal("unitPrice", ex.ParamName);
    }

    [Fact]
    public void Constructor_WithZeroUnitPrice_IsValid()
    {
        var item = new OrderItem(Guid.NewGuid(), "Brinde", 1, 0m);

        Assert.Equal(0m, item.UnitPrice);
        Assert.NotEqual(Guid.Empty, item.Id);
    }
}
