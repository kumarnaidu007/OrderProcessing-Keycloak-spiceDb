using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrderProcessing.Common;
using OrderProcessing.Dtos.Addresses;
using OrderProcessing.Models;
using OrderProcessing.Services;

namespace OrderProcessing.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = Roles.Customer)]
[Authorize(Policy = PolicyNames.AddressesManage)]
public class AddressesController : ControllerBase
{
    private readonly OrderProcessingContext _db;
    private readonly ILogger<AddressesController> _logger;
    private readonly IApplicationUserResolver _userResolver;

    public AddressesController(OrderProcessingContext db, ILogger<AddressesController> logger, IApplicationUserResolver userResolver)
    {
        _db = db;
        _logger = logger;
        _userResolver = userResolver;
    }

    private async Task<Customer?> GetCustomerForUserAsync(CancellationToken ct)
    {
        var userId = await _userResolver.TryGetUserIdAsync(User, ct);
        if (userId is null)
            return null;
        return await _db.Customers.FirstOrDefaultAsync(c => c.UserId == userId.Value, ct);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CustomerAddressResponse>>> List(CancellationToken ct)
    {
        _logger.LogInformation("Listing addresses for current user.");
        try
        {
            var customer = await GetCustomerForUserAsync(ct);
            if (customer is null)
            {
                _logger.LogWarning("Address list failed: no customer profile.");
                return Problem(statusCode: 400, detail: "No customer profile for this user.");
            }

            var rows = await _db.CustomerAddresses
                .AsNoTracking()
                .Where(a => a.CustomerId == customer.CustomerId)
                .OrderByDescending(a => a.IsDefault)
                .ThenBy(a => a.CreatedAtUtc)
                .ToListAsync(ct);

            _logger.LogInformation("Listed {Count} addresses for customer {CustomerId}.", rows.Count, customer.CustomerId);
            return Ok(rows.Select(Map).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while listing addresses.");
            return StatusCode(500, new { message = "Unexpected error while listing addresses." });
        }
    }

    [HttpGet("{addressId:long}")]
    public async Task<ActionResult<CustomerAddressResponse>> Get(long addressId, CancellationToken ct)
    {
        _logger.LogInformation("Fetching address {AddressId}.", addressId);
        try
        {
            var customer = await GetCustomerForUserAsync(ct);
            if (customer is null)
            {
                _logger.LogWarning("Address get failed: no customer profile.");
                return Problem(statusCode: 400, detail: "No customer profile for this user.");
            }

            var row = await _db.CustomerAddresses.AsNoTracking()
                .FirstOrDefaultAsync(a => a.CustomerAddressId == addressId && a.CustomerId == customer.CustomerId, ct);
            if (row is null)
            {
                _logger.LogWarning("Address {AddressId} not found for customer {CustomerId}.", addressId, customer.CustomerId);
                return NotFound();
            }

            return Ok(Map(row));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while fetching address {AddressId}.", addressId);
            return StatusCode(500, new { message = "Unexpected error while fetching address." });
        }
    }

    [HttpPost]
    public async Task<ActionResult<CustomerAddressResponse>> Create([FromBody] CreateCustomerAddressRequest request, CancellationToken ct)
    {
        _logger.LogInformation("Creating address with label {Label}.", request.Label ?? "");
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var userId = await _userResolver.TryGetUserIdAsync(User, ct);
            if (userId is null)
                return Unauthorized(new { message = "User is not mapped in application database." });
            var customer = await GetCustomerForUserAsync(ct);
            if (customer is null)
            {
                _logger.LogWarning("Address create failed: no customer profile.");
                return Problem(statusCode: 400, detail: "No customer profile for this user.");
            }

            var now = DateTime.UtcNow;
            if (request.IsDefault)
            {
                var existing = await _db.CustomerAddresses.Where(a => a.CustomerId == customer.CustomerId).ToListAsync(ct);
                foreach (var a in existing)
                    a.IsDefault = false;
            }

            var entity = new CustomerAddress
            {
                CustomerId = customer.CustomerId,
                Label = request.Label ?? "",
                RecipientName = request.RecipientName,
                Phone = request.Phone,
                AddressLine1 = request.AddressLine1,
                AddressLine2 = request.AddressLine2 ?? "",
                City = request.City,
                StateOrRegion = request.StateOrRegion ?? "",
                PostalCode = request.PostalCode,
                CountryCode = request.CountryCode.ToUpperInvariant(),
                IsDefault = request.IsDefault,
                CreatedAtUtc = now
            };
            _db.CustomerAddresses.Add(entity);
            await _db.SaveChangesAsync(ct);

            AuditLogWriter.Add(_db, nameof(CustomerAddress), entity.CustomerAddressId.ToString(), "address.create", userId.Value,
                new { entity.CustomerId, entity.Label });
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            _logger.LogInformation("Created address {AddressId} for customer {CustomerId}.", entity.CustomerAddressId, customer.CustomerId);
            return CreatedAtAction(nameof(Get), new { addressId = entity.CustomerAddressId }, Map(entity));
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            _logger.LogError(ex, "Error while creating address.");
            return StatusCode(500, new { message = "Unexpected error while creating address." });
        }
    }

    [HttpPut("{addressId:long}")]
    public async Task<ActionResult<CustomerAddressResponse>> Update(long addressId, [FromBody] UpdateCustomerAddressRequest request, CancellationToken ct)
    {
        _logger.LogInformation("Updating address {AddressId}.", addressId);
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var userId = await _userResolver.TryGetUserIdAsync(User, ct);
            if (userId is null)
                return Unauthorized(new { message = "User is not mapped in application database." });
            var customer = await GetCustomerForUserAsync(ct);
            if (customer is null)
            {
                _logger.LogWarning("Address update failed: no customer profile.");
                return Problem(statusCode: 400, detail: "No customer profile for this user.");
            }

            var entity = await _db.CustomerAddresses.FirstOrDefaultAsync(
                a => a.CustomerAddressId == addressId && a.CustomerId == customer.CustomerId, ct);
            if (entity is null)
            {
                _logger.LogWarning("Address {AddressId} not found for customer {CustomerId}.", addressId, customer.CustomerId);
                return NotFound();
            }

            if (request.IsDefault == true)
            {
                var siblings = await _db.CustomerAddresses.Where(a => a.CustomerId == customer.CustomerId).ToListAsync(ct);
                foreach (var a in siblings)
                    a.IsDefault = a.CustomerAddressId == addressId;
            }

            if (request.Label is not null) entity.Label = request.Label;
            if (request.RecipientName is not null) entity.RecipientName = request.RecipientName;
            if (request.Phone is not null) entity.Phone = request.Phone;
            if (request.AddressLine1 is not null) entity.AddressLine1 = request.AddressLine1;
            if (request.AddressLine2 is not null) entity.AddressLine2 = request.AddressLine2;
            if (request.City is not null) entity.City = request.City;
            if (request.StateOrRegion is not null) entity.StateOrRegion = request.StateOrRegion;
            if (request.PostalCode is not null) entity.PostalCode = request.PostalCode;
            if (request.CountryCode is not null) entity.CountryCode = request.CountryCode.ToUpperInvariant();
            if (request.IsDefault is not null) entity.IsDefault = request.IsDefault.Value;

            await _db.SaveChangesAsync(ct);
            AuditLogWriter.Add(_db, nameof(CustomerAddress), entity.CustomerAddressId.ToString(), "address.update", userId.Value, null);
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            _logger.LogInformation("Updated address {AddressId}.", addressId);
            return Ok(Map(entity));
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            _logger.LogError(ex, "Error while updating address {AddressId}.", addressId);
            return StatusCode(500, new { message = "Unexpected error while updating address." });
        }
    }

    [HttpDelete("{addressId:long}")]
    public async Task<IActionResult> Delete(long addressId, CancellationToken ct)
    {
        _logger.LogInformation("Deleting address {AddressId}.", addressId);
        try
        {
            var userId = await _userResolver.TryGetUserIdAsync(User, ct);
            if (userId is null)
                return Unauthorized(new { message = "User is not mapped in application database." });
            var customer = await GetCustomerForUserAsync(ct);
            if (customer is null)
            {
                _logger.LogWarning("Address delete failed: no customer profile.");
                return Problem(statusCode: 400, detail: "No customer profile for this user.");
            }

            var entity = await _db.CustomerAddresses.FirstOrDefaultAsync(
                a => a.CustomerAddressId == addressId && a.CustomerId == customer.CustomerId, ct);
            if (entity is null)
            {
                _logger.LogWarning("Address {AddressId} not found for delete.", addressId);
                return NotFound();
            }

            var usedOnOrder = await _db.OrderShippingSnapshots.AnyAsync(s => s.SourceCustomerAddressId == addressId, ct);
            if (usedOnOrder)
            {
                _logger.LogWarning("Address {AddressId} delete blocked because it is referenced.", addressId);
                return Conflict(new { message = "Address is referenced by past orders and cannot be deleted." });
            }

            _db.CustomerAddresses.Remove(entity);
            AuditLogWriter.Add(_db, nameof(CustomerAddress), addressId.ToString(), "address.delete", userId.Value, null);
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Deleted address {AddressId}.", addressId);
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while deleting address {AddressId}.", addressId);
            return StatusCode(500, new { message = "Unexpected error while deleting address." });
        }
    }

    private static CustomerAddressResponse Map(CustomerAddress a) => new()
    {
        CustomerAddressId = a.CustomerAddressId,
        Label = a.Label,
        RecipientName = a.RecipientName,
        Phone = a.Phone,
        AddressLine1 = a.AddressLine1,
        AddressLine2 = string.IsNullOrEmpty(a.AddressLine2) ? null : a.AddressLine2,
        City = a.City,
        StateOrRegion = string.IsNullOrEmpty(a.StateOrRegion) ? null : a.StateOrRegion,
        PostalCode = a.PostalCode,
        CountryCode = a.CountryCode,
        IsDefault = a.IsDefault,
        CreatedAtUtc = a.CreatedAtUtc
    };
}
