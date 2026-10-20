using MaltaShop.Inventory;
using MaltaShop.Shared;
using MassTransit;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.UseStructuredLogging();

var database = Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? throw new InvalidOperationException("DATABASE_URL is required");
var broker = Environment.GetEnvironmentVariable("BROKER_HOST") ?? "rabbitmq";

builder.Services.AddDbContext<InventoryDbContext>(options => options.UseNpgsql(database));
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<ReserveStockConsumer>();
    x.AddConsumer<ReleaseStockConsumer>();
    x.AddConsumer<CommitStockConsumer>();
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(broker, "/", h =>
        {
            h.Username(Environment.GetEnvironmentVariable("BROKER_USER") ?? "maltashop");
            h.Password(Environment.GetEnvironmentVariable("BROKER_PASSWORD") ?? "maltashop");
        });
        // Commands arrive on a named queue the process manager sends to; events
        // arrive on a second endpoint this service subscribes to.
        cfg.ReceiveEndpoint("maltashop-inventory-commands", e =>
        {
            e.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(2)));
            e.ConfigureConsumer<ReserveStockConsumer>(context);
            e.ConfigureConsumer<ReleaseStockConsumer>(context);
        });
        cfg.ReceiveEndpoint("maltashop-inventory-events", e =>
        {
            e.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(2)));
            e.ConfigureConsumer<CommitStockConsumer>(context);
        });
    });
});

var app = builder.Build();
app.UseCorrelationId();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
    for (var attempt = 1; attempt <= 30; attempt++)
    {
        try { await db.Database.EnsureCreatedAsync(); break; }
        catch when (attempt < 30) { await Task.Delay(TimeSpan.FromSeconds(2)); }
    }
}

app.MapHealthProbes(async () =>
{
    using var scope = app.Services.CreateScope();
    return await scope.ServiceProvider.GetRequiredService<InventoryDbContext>().Database.CanConnectAsync();
});

app.MapGet("/stock", async (InventoryDbContext db) =>
    await db.Stock.AsNoTracking().OrderBy(s => s.Sku)
        .Select(s => new { sku = s.Sku, description = s.Description, on_hand = s.OnHand,
                           held = s.Held, available = s.OnHand - s.Held })
        .ToListAsync());

app.MapGet("/holds/{orderId:guid}", async (Guid orderId, InventoryDbContext db) =>
{
    var hold = await db.Holds.AsNoTracking().FirstOrDefaultAsync(h => h.OrderId == orderId);
    return hold is null ? Results.NotFound(new { error = "no hold for that order" })
                        : Results.Ok(new { order_id = hold.OrderId, state = hold.State, reason = hold.Reason });
});

app.Run();
