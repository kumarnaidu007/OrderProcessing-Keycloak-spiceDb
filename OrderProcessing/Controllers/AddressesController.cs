using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrderProcessing.Common;
using OrderProcessing.Dtos.Addresses;
using OrderProcessing.Models;

namespace OrderProcessing.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = Roles.Customer)]
[Authorize(Policy = PolicyNames.AddressesManage)]
public class AddressesController : ControllerBase
{
    private readonly OrderProcessingContext _db;

    public AddressesController(OrderProcessingContext db)
    {
        _db = db;
    }

    private async Task<Customer?> GetCustomerForUserAsync(CancellationToken ct)
    {
        var userId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        return await _db.Customers.FirstOrDefaultAsync(c => c.UserId == userId, ct);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CustomerAddressResponse>>> List(CancellationToken ct)
    {
        var customer = await GetCustomerForUserAsync(ct);
        if (customer is null)
            return Problem(statusCode: 400, detail: "No customer profile for this user.");

        var rows = await _db.CustomerAddresses
            .AsNoTracking()
            .Where(a => a.CustomerId == customer.CustomerId)
            .OrderByDescending(a => a.IsDefault)
            .ThenBy(a => a.CreatedAtUtc)
            .ToListAsync(ct);

        return Ok(rows.Select(Map).ToList());
    }

    [HttpGet("{addressId:long}")]
    public async Task<ActionResult<CustomerAddressResponse>> Get(long addressId, CancellationToken ct)
    {
        var customer = await GetCustomerForUserAsync(ct);
        if (customer is null)
            return Problem(statusCode: 400, detail: "No customer profile for this user.");

        var row = await _db.CustomerAddresses.AsNoTracking()
            .FirstOrDefaultAsync(a => a.CustomerAddressId == addressId && a.CustomerId == customer.CustomerId, ct);
        if (row is null)
            return NotFound();
        return Ok(Map(row));
    }

    [HttpPost]
    public async Task<ActionResult<CustomerAddressResponse>> Create([FromBody] CreateCustomerAddressRequest request, CancellationToken ct)
    {
        var userId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var customer = await GetCustomerForUserAsync(ct);
        if (customer is null)
            return Problem(statusCode: 400, detail: "No customer profile for this user.");

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

        AuditLogWriter.Add(_db, nameof(CustomerAddress), entity.CustomerAddressId.ToString(), "address.create", userId,
            new { entity.CustomerId, entity.Label });
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Get), new { addressId = entity.CustomerAddressId }, Map(entity));
    }

    [HttpPut("{addressId:long}")]
    public async Task<ActionResult<CustomerAddressResponse>> Update(long addressId, [FromBody] UpdateCustomerAddressRequest request, CancellationToken ct)
    {
        var userId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var customer = await GetCustomerForUserAsync(ct);
        if (customer is null)
            return Problem(statusCode: 400, detail: "No customer profile for this user.");

        var entity = await _db.CustomerAddresses.FirstOrDefaultAsync(
            a => a.CustomerAddressId == addressId && a.CustomerId == customer.CustomerId, ct);
        if (entity is null)
            return NotFound();

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
        AuditLogWriter.Add(_db, nameof(CustomerAddress), entity.CustomerAddressId.ToString(), "address.update", userId, null);
        await _db.SaveChangesAsync(ct);

        return Ok(Map(entity));
    }

    [HttpDelete("{addressId:long}")]
    public async Task<IActionResult> Delete(long addressId, CancellationToken ct)
    {
        var userId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var customer = await GetCustomerForUserAsync(ct);
        if (customer is null)
            return Problem(statusCode: 400, detail: "No customer profile for this user.");

        var entity = await _db.CustomerAddresses.FirstOrDefaultAsync(
            a => a.CustomerAddressId == addressId && a.CustomerId == customer.CustomerId, ct);
        if (entity is null)
            return NotFound();

        var usedOnOrder = await _db.OrderShippingSnapshots.AnyAsync(s => s.SourceCustomerAddressId == addressId, ct);
        if (usedOnOrder)
            return Conflict(new { message = "Address is referenced by past orders and cannot be deleted." });

        _db.CustomerAddresses.Remove(entity);
        AuditLogWriter.Add(_db, nameof(CustomerAddress), addressId.ToString(), "address.delete", userId, null);
        await _db.SaveChangesAsync(ct);
        return NoContent();
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
