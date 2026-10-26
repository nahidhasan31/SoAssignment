using MaltaShop.Notification;
using MaltaShop.Shared;
using MassTransit;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.UseStructuredLogging();

var database = Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? throw new InvalidOperationException("DATABASE_URL is required");
var broker = Environment.GetEnvironmentVariable("BROKER_HOST") ?? "rabbitmq";

builder.Services.AddDbContext<NotificationDbContext>(options => options.UseNpgsql(database));
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
        cfg.ReceiveEndpoint("maltashop-notification", e =>
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
    var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
    for (var attempt = 1; attempt <= 30; attempt++)
    {
        try { await db.Database.EnsureCreatedAsync(); break; }
        catch when (attempt < 30) { await Task.Delay(TimeSpan.FromSeconds(2)); }
    }
}

app.MapHealthProbes(async () =>
{
    using var scope = app.Services.CreateScope();
    return await scope.ServiceProvider.GetRequiredService<NotificationDbContext>().Database.CanConnectAsync();
});

app.MapGet("/notifications", async (Guid? orderId, NotificationDbContext db) =>
{
    var query = db.Notifications.AsNoTracking();
    if (orderId is not null) query = query.Where(n => n.OrderId == orderId);
    return await query.OrderByDescending(n => n.CreatedAt).Take(50)
        .Select(n => new { order_id = n.OrderId, template = n.Template, subject = n.Subject,
                           body = n.Body, status = n.Status, correlation_id = n.CorrelationId,
                           created_at = n.CreatedAt })
        .ToListAsync();
});

app.Run();
