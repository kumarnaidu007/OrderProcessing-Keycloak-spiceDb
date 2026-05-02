using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using OrderProcessing.Models;

namespace OrderProcessing.Services;

public interface IApplicationUserResolver
{
    Task<long?> TryGetUserIdAsync(ClaimsPrincipal user, CancellationToken ct);
}

public sealed class ApplicationUserResolver : IApplicationUserResolver
{
    private readonly OrderProcessingContext _db;
    private readonly ILogger<ApplicationUserResolver> _logger;

    public ApplicationUserResolver(OrderProcessingContext db, ILogger<ApplicationUserResolver> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<long?> TryGetUserIdAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var sub = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(sub))
            return null;

        if (long.TryParse(sub, out var legacyNumericSub))
        {
            var byId = await _db.Users.AsNoTracking().Where(u => u.UserId == legacyNumericSub).Select(u => (long?)u.UserId).FirstOrDefaultAsync(ct);
            if (byId is not null)
                return byId;
        }

        var email = user.FindFirstValue(ClaimTypes.Email) ?? user.FindFirstValue("email");
        if (!string.IsNullOrWhiteSpace(email))
        {
            var byEmail = await _db.Users.AsNoTracking().Where(u => u.Email == email).Select(u => (long?)u.UserId).FirstOrDefaultAsync(ct);
            if (byEmail is not null)
                return byEmail;
        }

        var customerBySub = await _db.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.ExternalReference == sub, ct);
        if (customerBySub?.UserId is { } uid)
            return uid;

        return await ProvisionFromKeycloakAsync(user, sub, email, ct);
    }

    private async Task<long?> ProvisionFromKeycloakAsync(
        ClaimsPrincipal user,
        string sub,
        string? email,
        CancellationToken ct)
    {
        var syntheticEmail = string.IsNullOrWhiteSpace(email) ? $"{sub}@keycloak.local" : email!;
        var display = user.FindFirstValue("name")
                      ?? user.FindFirstValue(ClaimTypes.Name)
                      ?? syntheticEmail;
        if (display.Length > 200)
            display = display[..200];

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var existing = await _db.Users.FirstOrDefaultAsync(u => u.Email == syntheticEmail, ct);
            if (existing is not null)
            {
                await tx.CommitAsync(ct);
                return existing.UserId;
            }

            var now = DateTime.UtcNow;
            var newUser = new User
            {
                Email = syntheticEmail,
                DisplayName = display,
                Phone = "0000000000",
                IsActive = true,
                IsEmailVerified = true,
                IsPhoneVerified = false,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            _db.Users.Add(newUser);
            await _db.SaveChangesAsync(ct);

            var customer = new Customer
            {
                UserId = newUser.UserId,
                DisplayName = display,
                Email = syntheticEmail,
                Phone = newUser.Phone,
                ExternalReference = sub,
                IsActive = true,
                CreatedAtUtc = now
            };
            _db.Customers.Add(customer);
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            _logger.LogInformation("Provisioned local user {UserId} and customer from Keycloak sub {Sub}.", newUser.UserId, sub);
            return newUser.UserId;
        }
        catch (DbUpdateException)
        {
            await tx.RollbackAsync(ct);
            return await _db.Users.AsNoTracking().Where(u => u.Email == syntheticEmail).Select(u => (long?)u.UserId).FirstOrDefaultAsync(ct);
        }
    }
}
