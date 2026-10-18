using MaltaShop.Orchestration;
using MaltaShop.Shared;
using MassTransit;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.UseStructuredLogging();

var database = Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? throw new InvalidOperationException("DATABASE_URL is required");
var broker = Environment.GetEnvironmentVariable("BROKER_HOST") ?? "rabbitmq";

builder.Services.AddDbContext<OrchestrationDbContext>(options => options.UseNpgsql(database));

builder.Services.AddMassTransit(x =>
{
    // The state machine is registered with a database-backed repository, so an
    // instance survives a restart and two messages for the same order are
    // serialised by a row lock rather than by hope.
    x.AddSagaStateMachine<OrderStateMachine, OrderState>()
        .EntityFrameworkRepository(r =>
        {
            r.ConcurrencyMode = ConcurrencyMode.Pessimistic;
            r.ExistingDbContext<OrchestrationDbContext>();
            r.UsePostgres();
        });

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(broker, "/", h =>
        {
            h.Username(Environment.GetEnvironmentVariable("BROKER_USER") ?? "maltashop");
            h.Password(Environment.GetEnvironmentVariable("BROKER_PASSWORD") ?? "maltashop");
        });

        cfg.ReceiveEndpoint("maltashop-order-saga", e =>
        {
            // Retry the transient, redeliver the rest after a pause, and send what
            // still fails to the error queue rather than losing it.
            e.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(2)));
            e.UseDelayedRedelivery(r => r.Intervals(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)));
            e.ConfigureSaga<OrderState>(context);
        });
    });
});

var app = builder.Build();
app.UseCorrelationId();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OrchestrationDbContext>();
    for (var attempt = 1; attempt <= 30; attempt++)
    {
        try { await db.Database.EnsureCreatedAsync(); break; }
        catch when (attempt < 30) { await Task.Delay(TimeSpan.FromSeconds(2)); }
    }
}

app.MapHealthProbes(async () =>
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<OrchestrationDbContext>();
    return await db.Database.CanConnectAsync();
});

// The saga is queryable, which is the practical advantage of a process manager
// over a choreography: one row answers "where did this order stop?".
app.MapGet("/sagas/{orderId:guid}", async (Guid orderId, OrchestrationDbContext db) =>
{
    var saga = await db.Set<OrderState>().AsNoTracking()
        .FirstOrDefaultAsync(s => s.CorrelationId == orderId);
    return saga is null
        ? Results.NotFound(new { error = "no saga for that order" })
        : Results.Ok(new
        {
            order_id = saga.CorrelationId,
            state = saga.CurrentState,
            path = saga.Path,
            total_cents = saga.TotalCents,
            failure_reason = saga.FailureReason,
            psp_reference = saga.PspReference,
            started_at = saga.StartedAt,
            updated_at = saga.UpdatedAt,
        });
});

app.MapGet("/sagas", async (OrchestrationDbContext db) =>
    await db.Set<OrderState>().AsNoTracking()
        .OrderByDescending(s => s.UpdatedAt).Take(50)
        .Select(s => new { order_id = s.CorrelationId, state = s.CurrentState, path = s.Path })
        .ToListAsync());

app.Run();
