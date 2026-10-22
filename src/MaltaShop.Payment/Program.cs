using MaltaShop.Contracts;
using MaltaShop.Payment;
using MaltaShop.Shared;
using MassTransit;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.UseStructuredLogging();

var database = Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? throw new InvalidOperationException("DATABASE_URL is required");
var broker = Environment.GetEnvironmentVariable("BROKER_HOST") ?? "rabbitmq";

builder.Services.AddDbContext<PaymentDbContext>(options => options.UseNpgsql(database));
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<AuthorisePaymentConsumer>();
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(broker, "/", h =>
        {
            h.Username(Environment.GetEnvironmentVariable("BROKER_USER") ?? "maltashop");
            h.Password(Environment.GetEnvironmentVariable("BROKER_PASSWORD") ?? "maltashop");
        });
        cfg.ReceiveEndpoint("maltashop-payment-commands", e =>
        {
            // The provider is an external system: retry the transient failures here
            // rather than failing the saga and compensating unnecessarily.
            e.UseMessageRetry(r => r.Exponential(4, TimeSpan.FromSeconds(1),
                                                 TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(2)));
            e.ConfigureConsumer<AuthorisePaymentConsumer>(context);
        });
    });
});

var app = builder.Build();
app.UseCorrelationId();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
    for (var attempt = 1; attempt <= 30; attempt++)
    {
        try { await db.Database.EnsureCreatedAsync(); break; }
        catch when (attempt < 30) { await Task.Delay(TimeSpan.FromSeconds(2)); }
    }
}

app.MapHealthProbes(async () =>
{
    using var scope = app.Services.CreateScope();
    return await scope.ServiceProvider.GetRequiredService<PaymentDbContext>().Database.CanConnectAsync();
});

app.MapGet("/payments", async (Guid? orderId, PaymentDbContext db) =>
{
    var query = db.Payments.AsNoTracking();
    if (orderId is not null) query = query.Where(p => p.OrderId == orderId);
    return await query.OrderByDescending(p => p.CreatedAt).Take(50)
        .Select(p => new { order_id = p.OrderId, amount_cents = p.AmountCents, state = p.State,
                           psp_reference = p.PspReference, decline_reason = p.DeclineReason,
                           created_at = p.CreatedAt })
        .ToListAsync();
});

app.Run();
