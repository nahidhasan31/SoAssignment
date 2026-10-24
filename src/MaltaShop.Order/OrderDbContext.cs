using Microsoft.EntityFrameworkCore;

namespace MaltaShop.Order;

public class OrderRecord
{
    public Guid OrderId { get; set; }
    public string CustomerId { get; set; } = string.Empty;
    public string State { get; set; } = "PENDING";
    public long TotalCents { get; set; }
    public string Currency { get; set; } = "EUR";
    public string IdempotencyKey { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string? FailureReason { get; set; }
    public string LinesJson { get; set; } = "[]";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class OrderDbContext(DbContextOptions<OrderDbContext> options) : DbContext(options)
{
    public DbSet<OrderRecord> Orders => Set<OrderRecord>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        var order = model.Entity<OrderRecord>();
        order.HasKey(o => o.OrderId);
        // A repeated Idempotency-Key cannot create a second order: the database
        // refuses it, rather than the application remembering to check.
        order.HasIndex(o => o.IdempotencyKey).IsUnique();
        order.Property(o => o.CustomerId).HasMaxLength(64);
        order.Property(o => o.State).HasMaxLength(32);
        order.Property(o => o.IdempotencyKey).HasMaxLength(128);
        order.Property(o => o.CorrelationId).HasMaxLength(64);
        order.Property(o => o.FailureReason).HasMaxLength(128);
    }
}
