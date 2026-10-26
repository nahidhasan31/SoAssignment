using MaltaShop.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace MaltaShop.Notification;

/// <summary>Subscribes to outcomes. Nothing in checkout waits for this service, which
/// is the fix for the March incident.</summary>
public class OrderConfirmedConsumer(NotificationDbContext db, ILogger<OrderConfirmedConsumer> logger)
    : IConsumer<OrderConfirmed>
{
    public async Task Consume(ConsumeContext<OrderConfirmed> context)
    {
        var message = context.Message;
        if (await db.Notifications.AnyAsync(n => n.OrderId == message.OrderId && n.Template == "order-confirmed"))
        {
            logger.LogInformation("{Event} order={OrderId}", "notification.duplicate_ignored", message.OrderId);
            return;
        }
        db.Notifications.Add(new NotificationRecord
        {
            OrderId = message.OrderId,
            CustomerId = message.CustomerId,
            Template = "order-confirmed",
            Subject = $"MaltaShop order {message.OrderId.ToString()[..8]} confirmed",
            Body = $"Thank you. We have taken EUR {message.TotalCents / 100m:0.00} for {message.LineCount} item(s).",
            CorrelationId = message.CorrelationId,
        });
        await db.SaveChangesAsync();
        logger.LogInformation("{Event} order={OrderId} template={Template} correlation_id={CorrelationId}",
            "notification.sent", message.OrderId, "order-confirmed", message.CorrelationId);
    }
}

public class OrderCancelledConsumer(NotificationDbContext db, ILogger<OrderCancelledConsumer> logger)
    : IConsumer<OrderCancelled>
{
    public async Task Consume(ConsumeContext<OrderCancelled> context)
    {
        var message = context.Message;
        if (await db.Notifications.AnyAsync(n => n.OrderId == message.OrderId && n.Template == "order-cancelled"))
            return;
        db.Notifications.Add(new NotificationRecord
        {
            OrderId = message.OrderId,
            CustomerId = message.CustomerId,
            Template = "order-cancelled",
            Subject = $"MaltaShop order {message.OrderId.ToString()[..8]} could not be completed",
            Body = $"We are sorry: your order was not completed ({message.Reason}). You have not been charged.",
            CorrelationId = message.CorrelationId,
        });
        await db.SaveChangesAsync();
        logger.LogInformation("{Event} order={OrderId} template={Template} correlation_id={CorrelationId}",
            "notification.sent", message.OrderId, "order-cancelled", message.CorrelationId);
    }
}
