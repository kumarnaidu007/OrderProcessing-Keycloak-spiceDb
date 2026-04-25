using Microsoft.EntityFrameworkCore;
using OrderProcessing.Models;

namespace OrderProcessing.Common;

public static class DataSeeder
{
    public static async Task SeedAsync(OrderProcessingContext db, IConfiguration configuration, ILoggerFactory loggerFactory, CancellationToken ct = default)
    {
        var logger = loggerFactory.CreateLogger("DataSeeder");

        if (!await db.Roles.AnyAsync(ct))
        {
            db.Roles.Add(new Role { RoleName = Roles.Customer, Description = "Browse catalog and place orders" });
            db.Roles.Add(new Role { RoleName = Roles.Admin, Description = "Manage products and view all orders" });
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Seeded roles: {Roles}", $"{Roles.Customer}, {Roles.Admin}");
        }

        if (!await db.Permissions.AnyAsync(ct))
        {
            foreach (var code in PermissionCodes.All)
            {
                db.Permissions.Add(new Permission
                {
                    PermissionCode = code,
                    Description = $"Allows {code}"
                });
            }

            await db.SaveChangesAsync(ct);
            logger.LogInformation("Seeded {Count} permissions.", PermissionCodes.All.Count);
        }

        var allPerms = await db.Permissions.ToListAsync(ct);
        var adminRole = await db.Roles.Include(r => r.Permissions).FirstAsync(r => r.RoleName == Roles.Admin, ct);
        foreach (var code in PermissionCodes.Admin)
        {
            var p = allPerms.First(x => x.PermissionCode == code);
            if (adminRole.Permissions.All(x => x.PermissionId != p.PermissionId))
                adminRole.Permissions.Add(p);
        }

        var customerRole = await db.Roles.Include(r => r.Permissions).FirstAsync(r => r.RoleName == Roles.Customer, ct);
        foreach (var code in PermissionCodes.Customer)
        {
            var p = allPerms.First(x => x.PermissionCode == code);
            if (customerRole.Permissions.All(x => x.PermissionId != p.PermissionId))
                customerRole.Permissions.Add(p);
        }

        await db.SaveChangesAsync(ct);

        var adminEmail = configuration["Bootstrap:AdminEmail"] ?? "admin@local.test";
        var adminUser = await db.Users.FirstOrDefaultAsync(u => u.Email == adminEmail, ct);
        if (adminUser is null)
        {
            var user = new User
            {
                Email = adminEmail,
                DisplayName = "Administrator",
                Phone = "0000000000",
                IsActive = true,
                IsEmailVerified = true,
                IsPhoneVerified = false,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);
            adminUser = user;
            logger.LogInformation("Seeded admin user {Email}", adminEmail);
        }

        var adminRoleRef = await db.Roles.FirstAsync(r => r.RoleName == Roles.Admin, ct);
        var hasAdminRole = await db.UserRoles.AnyAsync(
            ur => ur.UserId == adminUser.UserId && ur.RoleId == adminRoleRef.RoleId,
            ct);
        if (!hasAdminRole)
        {
            db.UserRoles.Add(new UserRole
            {
                UserId = adminUser.UserId,
                RoleId = adminRoleRef.RoleId,
                AssignedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Assigned admin role to user {Email}", adminEmail);
        }

        if (!await db.Products.AnyAsync(ct))
        {
            var now = DateTime.UtcNow;
            db.Products.AddRange(
                new Product
                {
                    Name = "Wireless Mouse",
                    Price = 25.99m,
                    AvailableQuantity = 120,
                    ReservedQuantity = 0,
                    IsActive = true,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                    CreatedByUserId = adminUser.UserId,
                    UpdatedByUserId = adminUser.UserId
                },
                new Product
                {
                    Name = "Mechanical Keyboard",
                    Price = 79.50m,
                    AvailableQuantity = 80,
                    ReservedQuantity = 0,
                    IsActive = true,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                    CreatedByUserId = adminUser.UserId,
                    UpdatedByUserId = adminUser.UserId
                },
                new Product
                {
                    Name = "USB-C Hub",
                    Price = 39.00m,
                    AvailableQuantity = 100,
                    ReservedQuantity = 0,
                    IsActive = true,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                    CreatedByUserId = adminUser.UserId,
                    UpdatedByUserId = adminUser.UserId
                },
                new Product
                {
                    Name = "Noise Cancelling Headphones",
                    Price = 149.99m,
                    AvailableQuantity = 40,
                    ReservedQuantity = 0,
                    IsActive = true,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                    CreatedByUserId = adminUser.UserId,
                    UpdatedByUserId = adminUser.UserId
                });
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Seeded sample products.");
        }
    }
}
