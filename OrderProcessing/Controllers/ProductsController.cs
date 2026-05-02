using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrderProcessing.Common;
using OrderProcessing.Dtos.Products;
using OrderProcessing.Models;
using OrderProcessing.Services;

namespace OrderProcessing.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly OrderProcessingContext _db;
    private readonly ILogger<ProductsController> _logger;
    private readonly IApplicationUserResolver _userResolver;

    public ProductsController(OrderProcessingContext db, ILogger<ProductsController> logger, IApplicationUserResolver userResolver)
    {
        _db = db;
        _logger = logger;
        _userResolver = userResolver;
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<IReadOnlyList<ProductResponse>>> List(
        [FromQuery] bool includeInactive = false,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Listing products. IncludeInactive={IncludeInactive}", includeInactive);
        try
        {
            var q = _db.Products.AsNoTracking().AsQueryable();
            if (!includeInactive)
                q = q.Where(p => p.IsActive);
            var list = await q.OrderBy(p => p.Name).ToListAsync(ct);
            _logger.LogInformation("Listed {Count} products.", list.Count);
            return Ok(list.Select(Map).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while listing products.");
            return StatusCode(500, new { message = "Unexpected error while listing products." });
        }
    }

    [HttpGet("{productId:long}")]
    [AllowAnonymous]
    public async Task<ActionResult<ProductResponse>> Get(long productId, CancellationToken ct)
    {
        _logger.LogInformation("Fetching product {ProductId}.", productId);
        try
        {
            var p = await _db.Products.AsNoTracking().FirstOrDefaultAsync(x => x.ProductId == productId, ct);
            if (p is null)
            {
                _logger.LogWarning("Product {ProductId} not found.", productId);
                return NotFound();
            }

            if (!p.IsActive &&
                !User.HasClaim(AppClaims.Permission, PermissionCodes.ProductsManage) &&
                !User.IsInRole(Roles.Admin))
            {
                _logger.LogWarning("Inactive product {ProductId} access denied.", productId);
                return NotFound();
            }

            _logger.LogInformation("Fetched product {ProductId}.", productId);
            return Ok(Map(p));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while fetching product {ProductId}.", productId);
            return StatusCode(500, new { message = "Unexpected error while fetching product." });
        }
    }

    [HttpPost]
    [Authorize(Policy = PolicyNames.ProductsManage)]
    public async Task<ActionResult<ProductResponse>> Create([FromBody] CreateProductRequest request, CancellationToken ct)
    {
        _logger.LogInformation("Creating product {Name}.", request.Name);
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var userId = await _userResolver.TryGetUserIdAsync(User, ct);
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
            await tx.CommitAsync(ct);

            await _db.Entry(entity).ReloadAsync(ct);
            _logger.LogInformation("Created product {ProductId}.", entity.ProductId);
            return CreatedAtAction(nameof(Get), new { productId = entity.ProductId }, Map(entity));
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            _logger.LogError(ex, "Error while creating product {Name}.", request.Name);
            return StatusCode(500, new { message = "Unexpected error while creating product." });
        }
    }

    [HttpPut("{productId:long}")]
    [Authorize(Policy = PolicyNames.ProductsManage)]
    public async Task<ActionResult<ProductResponse>> Update(long productId, [FromBody] UpdateProductRequest request, CancellationToken ct)
    {
        _logger.LogInformation("Updating product {ProductId}.", productId);
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var userId = await _userResolver.TryGetUserIdAsync(User, ct);
            var entity = await _db.Products.FirstOrDefaultAsync(p => p.ProductId == productId, ct);
            if (entity is null)
            {
                _logger.LogWarning("Product {ProductId} not found for update.", productId);
                return NotFound();
            }

            if (!string.IsNullOrWhiteSpace(request.RowVersion))
            {
                try
                {
                    var bytes = Convert.FromBase64String(request.RowVersion);
                    _db.Entry(entity).Property(e => e.RowVersion).OriginalValue = bytes;
                }
                catch (FormatException)
                {
                    _logger.LogWarning("Invalid row version for product {ProductId}.", productId);
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
                _logger.LogWarning("Concurrency conflict while updating product {ProductId}.", productId);
                return Conflict(new { message = "Product was modified by another request. Refresh and retry." });
            }

            AuditLogWriter.Add(_db, nameof(Product), entity.ProductId.ToString(), "product.update", userId, null);
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            await _db.Entry(entity).ReloadAsync(ct);
            _logger.LogInformation("Updated product {ProductId}.", productId);
            return Ok(Map(entity));
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            _logger.LogError(ex, "Error while updating product {ProductId}.", productId);
            return StatusCode(500, new { message = "Unexpected error while updating product." });
        }
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
