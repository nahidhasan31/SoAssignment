using System.Text.Json;
using MaltaShop.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace MaltaShop.Inventory;

/// <summary>Holds stock without taking it. The hold is a row anyone can read, not a
/// database lock, so nothing is held across a network call.</summary>
public class ReserveStockConsumer(InventoryDbContext db, ILogger<ReserveStockConsumer> logger)
    : IConsumer<ReserveStock>
{
    public async Task Consume(ConsumeContext<ReserveStock> context)
    {
        var orderId = context.Message.OrderId;
        var existing = await db.Holds.FirstOrDefaultAsync(h => h.OrderId == orderId);
        if (existing is not null)
        {
            // Redelivered. Answer with the decision already taken rather than retaking it.
            logger.LogInformation("{Event} order={OrderId} state={State}",
                "stock.reserve_replayed", orderId, existing.State);
            await Reply(context, existing.State == "HELD", existing.Reason);
            return;
        }

        var lines = context.Message.Lines.OrderBy(l => l.Sku, StringComparer.Ordinal).ToList();
        foreach (var line in lines)
        {
            var item = await db.Stock.FirstOrDefaultAsync(s => s.Sku == line.Sku);
            if (item is null) { await Reject(context, orderId, lines, $"unknown_sku:{line.Sku}"); return; }
            if (item.OnHand - item.Held < line.Quantity)
            { await Reject(context, orderId, lines, $"insufficient_stock:{line.Sku}"); return; }
        }

        foreach (var line in lines)
        {
            var item = await db.Stock.FirstAsync(s => s.Sku == line.Sku);
            item.Held += line.Quantity;
        }
        db.Holds.Add(new Hold { OrderId = orderId, State = "HELD", LinesJson = JsonSerializer.Serialize(lines) });
        await db.SaveChangesAsync();

        logger.LogInformation("{Event} order={OrderId} lines={Lines} correlation_id={CorrelationId}",
            "stock.reserved", orderId, lines.Count, context.Message.CorrelationId);
        await context.Publish(new StockReserved { OrderId = orderId, CorrelationId = context.Message.CorrelationId });
    }

    private async Task Reject(ConsumeContext<ReserveStock> context, Guid orderId,
                              List<OrderLine> lines, string reason)
    {
        db.Holds.Add(new Hold { OrderId = orderId, State = "REJECTED",
                                LinesJson = JsonSerializer.Serialize(lines), Reason = reason });
        await db.SaveChangesAsync();
        logger.LogWarning("{Event} order={OrderId} reason={Reason}", "stock.rejected", orderId, reason);
        await context.Publish(new StockRejected { OrderId = orderId, Reason = reason,
                                                  CorrelationId = context.Message.CorrelationId });
    }

    private static async Task Reply(ConsumeContext<ReserveStock> context, bool held, string? reason) =>
        await (held
            ? context.Publish(new StockReserved { OrderId = context.Message.OrderId,
                                                  CorrelationId = context.Message.CorrelationId })
            : context.Publish(new StockRejected { OrderId = context.Message.OrderId,
                                                  Reason = reason ?? "already_rejected",
                                                  CorrelationId = context.Message.CorrelationId }));
}

/// <summary>The compensating action. Safe to run twice, and safe to run when nothing
/// was ever held, because the process manager may redeliver it.</summary>
public class ReleaseStockConsumer(InventoryDbContext db, ILogger<ReleaseStockConsumer> logger)
    : IConsumer<ReleaseStock>
{
    public async Task Consume(ConsumeContext<ReleaseStock> context)
    {
        var hold = await db.Holds.FirstOrDefaultAsync(h => h.OrderId == context.Message.OrderId);
        if (hold is null || hold.State != "HELD")
        {
            logger.LogInformation("{Event} order={OrderId}", "stock.release_noop", context.Message.OrderId);
            await context.Publish(new StockReleased { OrderId = context.Message.OrderId,
                                                      CorrelationId = context.Message.CorrelationId });
            return;
        }

        foreach (var line in JsonSerializer.Deserialize<List<OrderLine>>(hold.LinesJson) ?? [])
        {
            var item = await db.Stock.FirstOrDefaultAsync(s => s.Sku == line.Sku);
            if (item is not null) item.Held = Math.Max(item.Held - line.Quantity, 0);
        }
        hold.State = "RELEASED";
        hold.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        logger.LogInformation("{Event} order={OrderId} correlation_id={CorrelationId}",
            "stock.released", context.Message.OrderId, context.Message.CorrelationId);
        await context.Publish(new StockReleased { OrderId = context.Message.OrderId,
                                                  CorrelationId = context.Message.CorrelationId });
    }
}

/// <summary>Once the order is confirmed the hold becomes a real deduction.</summary>
public class CommitStockConsumer(InventoryDbContext db, ILogger<CommitStockConsumer> logger)
    : IConsumer<OrderConfirmed>
{
    public async Task Consume(ConsumeContext<OrderConfirmed> context)
    {
        var hold = await db.Holds.FirstOrDefaultAsync(h => h.OrderId == context.Message.OrderId);
        if (hold is null || hold.State != "HELD") return;
        foreach (var line in JsonSerializer.Deserialize<List<OrderLine>>(hold.LinesJson) ?? [])
        {
            var item = await db.Stock.FirstOrDefaultAsync(s => s.Sku == line.Sku);
            if (item is null) continue;
            item.OnHand -= line.Quantity;
            item.Held = Math.Max(item.Held - line.Quantity, 0);
        }
        hold.State = "COMMITTED";
        hold.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        logger.LogInformation("{Event} order={OrderId}", "stock.committed", context.Message.OrderId);
    }
}
