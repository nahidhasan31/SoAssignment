using Microsoft.EntityFrameworkCore;

namespace MaltaShop.Inventory;

public class StockItem
{
    public string Sku { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int OnHand { get; set; }
    public int Held { get; set; }
}

public class Hold
{
    public Guid OrderId { get; set; }
    public string State { get; set; } = "HELD";     // HELD | RELEASED | COMMITTED | REJECTED
    public string LinesJson { get; set; } = "[]";
    public string? Reason { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    public DbSet<StockItem> Stock => Set<StockItem>();
    public DbSet<Hold> Holds => Set<Hold>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<StockItem>().HasKey(s => s.Sku);
        model.Entity<StockItem>().Property(s => s.Sku).HasMaxLength(32);
        model.Entity<Hold>().HasKey(h => h.OrderId);
        model.Entity<Hold>().Property(h => h.State).HasMaxLength(16);
        model.Entity<Hold>().Property(h => h.Reason).HasMaxLength(128);

        model.Entity<StockItem>().HasData(
            new StockItem { Sku = "OVEN-PYRO", Description = "Attard pyrolytic oven", OnHand = 21 },
            new StockItem { Sku = "DISHW-60", Description = "Siggiewi 60 cm dishwasher", OnHand = 34 },
            new StockItem { Sku = "MICRO-25L", Description = "Mosta 25 L microwave", OnHand = 96 },
            new StockItem { Sku = "HOB-IND4", Description = "Qrendi four-zone induction hob", OnHand = 28 },
            new StockItem { Sku = "FRIDGE-XL", Description = "Gharghur XL fridge-freezer", OnHand = 15 },
            new StockItem { Sku = "AWAIT-STOCK", Description = "Mellieha air purifier (awaiting stock)", OnHand = 0 });
    }
}
