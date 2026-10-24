using MaltaShop.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace MaltaShop.Order;

/// <summary>The Order service owns the order. It learns the outcome from the
/// events the process manager publishes, and never from a direct call.</summary>
public class OrderConfirmedConsumer(OrderDbContext db, ILogger<OrderConfirmedConsumer> logger)
    : IConsumer<OrderConfirmed>
{
    public async Task Consume(ConsumeContext<OrderConfirmed> context)
    {
        var order = await db.Orders.FirstOrDefaultAsync(o => o.OrderId == context.Message.OrderId);
        if (order is null) return;
        order.State = "CONFIRMED";
        order.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        logger.LogInformation("{Event} order={OrderId} correlation_id={CorrelationId}",
            "order.confirmed", order.OrderId, context.Message.CorrelationId);
    }
}

public class OrderCancelledConsumer(OrderDbContext db, ILogger<OrderCancelledConsumer> logger)
    : IConsumer<OrderCancelled>
{
    public async Task Consume(ConsumeContext<OrderCancelled> context)
    {
        var order = await db.Orders.FirstOrDefaultAsync(o => o.OrderId == context.Message.OrderId);
        if (order is null) return;
        order.State = context.Message.Reason.StartsWith("insufficient_stock", StringComparison.Ordinal)
                      || context.Message.Reason.StartsWith("unknown_sku", StringComparison.Ordinal)
            ? "REJECTED" : "CANCELLED";
        order.FailureReason = context.Message.Reason;
        order.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        logger.LogWarning("{Event} order={OrderId} reason={Reason} correlation_id={CorrelationId}",
            "order.cancelled", order.OrderId, context.Message.Reason, context.Message.CorrelationId);
    }
}
