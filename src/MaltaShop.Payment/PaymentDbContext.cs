using Microsoft.EntityFrameworkCore;

namespace MaltaShop.Payment;

public class PaymentRecord
{
    public Guid PaymentId { get; set; } = Guid.NewGuid();
    /// <summary>Unique: a redelivered command cannot produce a second charge.</summary>
    public Guid OrderId { get; set; }
    public string CustomerId { get; set; } = string.Empty;
    public long AmountCents { get; set; }
    public string State { get; set; } = "AUTHORISED";
    /// <summary>The provider's reference. A card number is never stored, which keeps
    /// the rest of the platform outside the PCI DSS boundary.</summary>
    public string? PspReference { get; set; }
    public string? DeclineReason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class PaymentDbContext(DbContextOptions<PaymentDbContext> options) : DbContext(options)
{
    public DbSet<PaymentRecord> Payments => Set<PaymentRecord>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        var payment = model.Entity<PaymentRecord>();
        payment.HasKey(p => p.PaymentId);
        payment.HasIndex(p => p.OrderId).IsUnique();
        payment.Property(p => p.CustomerId).HasMaxLength(64);
        payment.Property(p => p.State).HasMaxLength(16);
        payment.Property(p => p.PspReference).HasMaxLength(64);
        payment.Property(p => p.DeclineReason).HasMaxLength(128);
    }
}
