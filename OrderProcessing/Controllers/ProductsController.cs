using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrderProcessing.Common;
using OrderProcessing.Dtos.Products;
using OrderProcessing.Models;

namespace OrderProcessing.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly OrderProcessingContext _db;

    public ProductsController(OrderProcessingContext db)
    {
        _db = db;
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<IReadOnlyList<ProductResponse>>> List(
        [FromQuery] bool includeInactive = false,
        CancellationToken ct = default)
    {
        var q = _db.Products.AsNoTracking().AsQueryable();
        if (!includeInactive)
            q = q.Where(p => p.IsActive);
        var list = await q.OrderBy(p => p.Name).ToListAsync(ct);
        return Ok(list.Select(Map).ToList());
    }

    [HttpGet("{productId:long}")]
    [AllowAnonymous]
    public async Task<ActionResult<ProductResponse>> Get(long productId, CancellationToken ct)
    {
        var p = await _db.Products.AsNoTracking().FirstOrDefaultAsync(x => x.ProductId == productId, ct);
        if (p is null)
            return NotFound();
        if (!p.IsActive && !User.HasClaim(AppClaims.Permission, PermissionCodes.ProductsManage))
            return NotFound();
        return Ok(Map(p));
    }

    [HttpPost]
    [Authorize(Policy = PolicyNames.ProductsManage)]
    public async Task<ActionResult<ProductResponse>> Create([FromBody] CreateProductRequest request, CancellationToken ct)
    {
        var userId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var now = DateTime.UtcNow;
        var entity = new Product
        {
            Name = request.Name,
            Price = request.Price,
            AvailableQuantity = request.AvailableQuantity,
            ReservedQuantity = 0,
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            CreatedByUserId = userId,
            UpdatedByUserId = userId
        };
        _db.Products.Add(entity);
        await _db.SaveChangesAsync(ct);
        AuditLogWriter.Add(_db, nameof(Product), entity.ProductId.ToString(), "product.create", userId,
            new { entity.Name, entity.Price });
        await _db.SaveChangesAsync(ct);
        await _db.Entry(entity).ReloadAsync(ct);
        return CreatedAtAction(nameof(Get), new { productId = entity.ProductId }, Map(entity));
    }

    [HttpPut("{productId:long}")]
    [Authorize(Policy = PolicyNames.ProductsManage)]
    public async Task<ActionResult<ProductResponse>> Update(long productId, [FromBody] UpdateProductRequest request, CancellationToken ct)
    {
        var userId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var entity = await _db.Products.FirstOrDefaultAsync(p => p.ProductId == productId, ct);
        if (entity is null)
            return NotFound();

        if (!string.IsNullOrWhiteSpace(request.RowVersion))
        {
            try
            {
                var bytes = Convert.FromBase64String(request.RowVersion);
                _db.Entry(entity).Property(e => e.RowVersion).OriginalValue = bytes;
            }
            catch (FormatException)
            {
                return BadRequest(new { message = "Invalid RowVersion (expected Base64)." });
            }
        }

        if (request.Name is not null) entity.Name = request.Name;
        if (request.Price is not null) entity.Price = request.Price.Value;
        if (request.AvailableQuantity is not null) entity.AvailableQuantity = request.AvailableQuantity.Value;
        if (request.IsActive is not null) entity.IsActive = request.IsActive.Value;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        entity.UpdatedByUserId = userId;

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { message = "Product was modified by another request. Refresh and retry." });
        }

        AuditLogWriter.Add(_db, nameof(Product), entity.ProductId.ToString(), "product.update", userId, null);
        await _db.SaveChangesAsync(ct);

        await _db.Entry(entity).ReloadAsync(ct);
        return Ok(Map(entity));
    }

    private static ProductResponse Map(Product p) => new()
    {
        ProductId = p.ProductId,
        Name = p.Name,
        Price = p.Price,
        AvailableQuantity = p.AvailableQuantity,
        IsActive = p.IsActive,
        RowVersion = p.RowVersion is { Length: > 0 } ? Convert.ToBase64String(p.RowVersion) : null
    };
}
