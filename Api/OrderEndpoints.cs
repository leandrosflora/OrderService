using Microsoft.EntityFrameworkCore;
using OrderService.Application;
using OrderService.Contracts;
using OrderService.Infrastructure.Persistence;

namespace OrderService.Api;

public static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/orders").WithTags("Orders");

        group.MapGet("/", async (
            Guid? buyerId,
            OrderDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var query = dbContext.Orders.AsNoTracking().AsQueryable();

            if (buyerId is { } buyer)
            {
                query = query.Where(x => x.BuyerId == buyer);
            }

            var orders = await query
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => new OrderListItemResponse(
                    x.Id,
                    x.Status.ToString(),
                    x.ItemsTotal,
                    x.ShippingPrice,
                    x.TotalAmount,
                    x.Currency,
                    x.CreatedAt,
                    x.ShipmentId))
                .ToListAsync(cancellationToken);

            return Results.Ok(orders);
        });

        group.MapGet("/{orderId:guid}", async (
            Guid orderId,
            OrderDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var order = await dbContext.Orders
                .AsNoTracking()
                .Include(x => x.Items)
                .SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);

            if (order is null)
            {
                return Results.NotFound();
            }

            return Results.Ok(new OrderResponse(
                order.Id,
                order.CheckoutId,
                order.BuyerId,
                order.SellerId,
                order.Status.ToString(),
                order.Currency,
                order.ItemsTotal,
                order.ShippingPrice,
                order.TotalAmount,
                order.ShippingPromiseId,
                order.PricingQuoteId,
                order.InventoryReservationId,
                order.CapacityReservationId,
                order.PaymentAuthorizationId,
                order.ShipmentId,
                order.CreatedAt,
                order.UpdatedAt,
                order.ConfirmedAt,
                order.CancelledAt,
                order.Items.Select(x => new OrderItemResponse(
                    x.SkuId,
                    x.Title,
                    x.Quantity,
                    x.UnitPrice,
                    x.TotalPrice)).ToList()));
        });

        group.MapPost("/{orderId:guid}/cancel", async (
            Guid orderId,
            CancelOrderRequest request,
            HttpContext context,
            OrderCancellationService service,
            CancellationToken cancellationToken) =>
        {
            var idempotencyKey = context.Request.Headers["Idempotency-Key"].ToString();

            if (string.IsNullOrWhiteSpace(idempotencyKey))
            {
                return Results.BadRequest(new { error = "Idempotency-Key is required" });
            }

            await service.CancelAsync(orderId, request.Reason, idempotencyKey, cancellationToken);

            return Results.Accepted($"/orders/{orderId}");
        });

        return app;
    }
}
