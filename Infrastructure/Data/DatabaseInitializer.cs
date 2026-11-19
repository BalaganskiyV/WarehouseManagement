using Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
namespace Infrastructure.Data;
public static class DatabaseInitializer
{
    public static async Task SeedAsync(AppDbContext db, IPasswordHasher<User> hasher, string? adminPassword, string? managerPassword, CancellationToken ct = default)
    {
        foreach (var name in new[] { "Admin", "Manager", "Clerk" })
            if (!await db.Roles.AnyAsync(x => x.Name == name, ct)) db.Roles.Add(new Role { Name = name });
        if (!await db.Products.AnyAsync(ct)) db.Products.AddRange(
            new Product { Name = "Ноутбук ASUS", Sku = "ASUS-001", Price = 60000 },
            new Product { Name = "Мышь Logitech", Sku = "LOGI-001", Price = 1500 },
            new Product { Name = "Монитор Samsung", Sku = "SAMS-001", Price = 20000 });
        await db.SaveChangesAsync(ct);
        foreach (var item in new[] { (Name: "admin", Role: "Admin", Password: adminPassword), (Name: "manager", Role: "Manager", Password: managerPassword) })
        {
            if (string.IsNullOrEmpty(item.Password) || await db.Users.AnyAsync(x => x.Username == item.Name, ct)) continue;
            if (item.Password.Length < 12 || item.Password.Length > 128) throw new InvalidOperationException("Bootstrap passwords must contain 12 to 128 characters.");
            var user = new User { Username = item.Name, Email = item.Name + "@example.invalid", Role = await db.Roles.SingleAsync(x => x.Name == item.Role, ct) };
            user.PasswordHash = hasher.HashPassword(user, item.Password); db.Users.Add(user);
        }
        await db.SaveChangesAsync(ct);
    }
}
