using Domain.Entities;
using Microsoft.EntityFrameworkCore;
namespace Infrastructure.Data;
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<WarehouseEvent> WarehouseEvents => Set<WarehouseEvent>();
    public DbSet<Product> Products => Set<Product>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>().Property(x => x.Username).HasMaxLength(50);
        b.Entity<User>().HasIndex(x => x.Username).IsUnique();
        b.Entity<User>().Property(x => x.Email).HasMaxLength(254);
        b.Entity<Role>().Property(x => x.Name).HasMaxLength(30);
        b.Entity<Role>().HasIndex(x => x.Name).IsUnique();
        b.Entity<Product>().Property(x => x.Sku).HasMaxLength(50);
        b.Entity<Product>().HasIndex(x => x.Sku).IsUnique();
        b.Entity<Product>().Property(x => x.Price).HasPrecision(18, 2);
        b.Entity<WarehouseEvent>().HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<WarehouseEvent>().HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<WarehouseEvent>().HasIndex(x => x.EventDate);
        b.Entity<WarehouseEvent>().Property(x => x.EventType).HasMaxLength(20);
        b.Entity<WarehouseEvent>().Property(x => x.WarehouseZone).HasMaxLength(10);
        b.Entity<WarehouseEvent>().Property(x => x.Description).HasMaxLength(2000);
        b.Entity<WarehouseEvent>().ToTable(t =>
        {
            t.HasCheckConstraint("CK_WarehouseEvents_Type", "[EventType] IN ('Arrival','Departure','Inventory')");
            t.HasCheckConstraint("CK_WarehouseEvents_Quantity", "[Quantity] >= 0 AND ([EventType] = 'Inventory' OR [Quantity] > 0)");
        });
    }
}
