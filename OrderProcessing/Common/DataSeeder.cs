using Microsoft.EntityFrameworkCore;
using OrderProcessing.Models;

namespace OrderProcessing.Common;

public static class DataSeeder
{
    public static async Task SeedAsync(OrderProcessingContext db, ILoggerFactory loggerFactory, CancellationToken ct = default)
    {
        var logger = loggerFactory.CreateLogger("DataSeeder");

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
                    RowVersion = new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 },
                    IsActive = true,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                    CreatedByUserId = null,
                    UpdatedByUserId = null
                },
                new Product
                {
                    Name = "Mechanical Keyboard",
                    Price = 79.50m,
                    AvailableQuantity = 80,
                    ReservedQuantity = 0,
                    RowVersion = new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 },
                    IsActive = true,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                    CreatedByUserId = null,
                    UpdatedByUserId = null
                },
                new Product
                {
                    Name = "USB-C Hub",
                    Price = 39.00m,
                    AvailableQuantity = 100,
                    ReservedQuantity = 0,
                    RowVersion = new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 },
                    IsActive = true,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                    CreatedByUserId = null,
                    UpdatedByUserId = null
                },
                new Product
                {
                    Name = "Noise Cancelling Headphones",
                    Price = 149.99m,
                    AvailableQuantity = 40,
                    ReservedQuantity = 0,
                    RowVersion = new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 },
                    IsActive = true,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                    CreatedByUserId = null,
                    UpdatedByUserId = null
                });
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Seeded sample products.");
        }
    }
}
