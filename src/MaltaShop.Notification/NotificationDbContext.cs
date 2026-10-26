using Microsoft.EntityFrameworkCore;

namespace MaltaShop.Notification;

public class NotificationRecord
{
    public Guid NotificationId { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public string CustomerId { get; set; } = string.Empty;
    public string Channel { get; set; } = "email";
    public string Template { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string Status { get; set; } = "SENT";
    public string CorrelationId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class NotificationDbContext(DbContextOptions<NotificationDbContext> options) : DbContext(options)
{
    public DbSet<NotificationRecord> Notifications => Set<NotificationRecord>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        var record = model.Entity<NotificationRecord>();
        record.HasKey(n => n.NotificationId);
        record.HasIndex(n => new { n.OrderId, n.Template }).IsUnique();   // one message per outcome
        record.Property(n => n.Template).HasMaxLength(48);
        record.Property(n => n.Subject).HasMaxLength(200);
        record.Property(n => n.CorrelationId).HasMaxLength(64);
    }
}
