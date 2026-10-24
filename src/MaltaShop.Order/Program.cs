using System.Text.Json;
using MaltaShop.Contracts;
using MaltaShop.Order;
using MaltaShop.Shared;
using MassTransit;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.UseStructuredLogging();

var database = Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? throw new InvalidOperationException("DATABASE_URL is required");
var broker = Environment.GetEnvironmentVariable("BROKER_HOST") ?? "rabbitmq";

builder.Services.AddDbContext<OrderDbContext>(options => options.UseNpgsql(database));
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<OrderConfirmedConsumer>();
    x.AddConsumer<OrderCancelledConsumer>();
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(broker, "/", h =>
        {
            h.Username(Environment.GetEnvironmentVariable("BROKER_USER") ?? "maltashop");
            h.Password(Environment.GetEnvironmentVariable("BROKER_PASSWORD") ?? "maltashop");
        });
        cfg.ReceiveEndpoint("maltashop-order-outcomes", e =>
        {
            e.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(2)));
            e.ConfigureConsumer<OrderConfirmedConsumer>(context);
            e.ConfigureConsumer<OrderCancelledConsumer>(context);
        });
    });
});

var app = builder.Build();
app.UseCorrelationId();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
    for (var attempt = 1; attempt <= 30; attempt++)
    {
        try { await db.Database.EnsureCreatedAsync(); break; }
        catch when (attempt < 30) { await Task.Delay(TimeSpan.FromSeconds(2)); }
    }
}

app.MapHealthProbes(async () =>
{
    using var scope = app.Services.CreateScope();
    return await scope.ServiceProvider.GetRequiredService<OrderDbContext>().Database.CanConnectAsync();
});

// A local replica of the catalogue's prices: checkout never calls another service.
var prices = new Dictionary<string, long>
{
    ["OVEN-PYRO"] = 67_900, ["DISHW-60"] = 45_900, ["MICRO-25L"] = 13_950,
    ["HOB-IND4"] = 52_900, ["FRIDGE-XL"] = 129_900, ["AWAIT-STOCK"] = 24_900,
};

app.MapPost("/orders", async (HttpContext http, OrderDbContext db, IPublishEndpoint publish,
                              ILogger<Program> logger) =>
{
    var correlationId = http.CorrelationId();
    var idempotencyKey = http.Request.Headers["Idempotency-Key"].FirstOrDefault() ?? Guid.NewGuid().ToString();
    var body = await JsonSerializer.DeserializeAsync<JsonElement>(http.Request.Body);

    if (!body.TryGetProperty("customer_id", out var customerElement)
        || !body.TryGetProperty("items", out var itemsElement)
        || itemsElement.ValueKind != JsonValueKind.Array || itemsElement.GetArrayLength() == 0)
        return Results.BadRequest(new { error = "customer_id and a non-empty items array are required" });

    var existing = await db.Orders.AsNoTracking()
        .FirstOrDefaultAsync(o => o.IdempotencyKey == idempotencyKey);
    if (existing is not null)
    {
        logger.LogInformation("{Event} order={OrderId}", "order.idempotent_replay", existing.OrderId);
        return Results.Ok(new { order_id = existing.OrderId, state = existing.State, replayed = true });
    }

    var lines = new List<OrderLine>();
    long total = 0;
    foreach (var item in itemsElement.EnumerateArray())
    {
        var sku = item.GetProperty("sku").GetString() ?? "";
        var quantity = item.GetProperty("quantity").GetInt32();
        if (!prices.TryGetValue(sku, out var unit))
            return Results.UnprocessableEntity(new { error = $"unknown sku {sku}" });
        if (quantity <= 0)
            return Results.UnprocessableEntity(new { error = $"invalid quantity for {sku}" });
        total += unit * quantity;
        lines.Add(new OrderLine(sku, quantity));
    }

    var order = new OrderRecord
    {
        OrderId = Guid.NewGuid(),
        CustomerId = customerElement.GetString() ?? "",
        TotalCents = total,
        IdempotencyKey = idempotencyKey,
        CorrelationId = correlationId,
        LinesJson = JsonSerializer.Serialize(lines),
    };
    db.Orders.Add(order);
    await db.SaveChangesAsync();

    await publish.Publish(new OrderSubmitted
    {
        OrderId = order.OrderId,
        CustomerId = order.CustomerId,
        TotalCents = order.TotalCents,
        Lines = lines,
        CorrelationId = correlationId,
    }, ctx => ctx.SetCorrelation(correlationId));

    logger.LogInformation("{Event} order={OrderId} total={TotalCents} lines={Lines}",
        "order.accepted", order.OrderId, order.TotalCents, lines.Count);

    http.Response.Headers["Location"] = $"/orders/{order.OrderId}";
    return Results.Created($"/orders/{order.OrderId}", new
    {
        order_id = order.OrderId,
        state = order.State,
        total_cents = order.TotalCents,
        currency = order.Currency,
        correlation_id = correlationId,
        message = "Order received. Stock and payment are being confirmed.",
    });
});

app.MapGet("/orders/{orderId:guid}", async (Guid orderId, OrderDbContext db) =>
{
    var order = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.OrderId == orderId);
    return order is null
        ? Results.NotFound(new { error = "order not found" })
        : Results.Ok(new
        {
            order_id = order.OrderId,
            customer_id = order.CustomerId,
            state = order.State,
            total_cents = order.TotalCents,
            currency = order.Currency,
            failure_reason = order.FailureReason,
            correlation_id = order.CorrelationId,
            items = JsonSerializer.Deserialize<List<OrderLine>>(order.LinesJson),
            created_at = order.CreatedAt,
            updated_at = order.UpdatedAt,
        });
});

app.Run();
