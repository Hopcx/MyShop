using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyShop.Api.Common;
using MyShop.Api.Domain;
using MyShop.Api.Infrastructure;

namespace MyShop.Api.Modules;

/// <summary>
/// Tồn kho: mọi thay đổi đều ghi vào ledger InventoryMovement (append-only, có thể replay để dựng lại số dư)
/// và phát sự kiện "InventoryChanged" qua outbox để đồng bộ hệ thống kho/WMS bên ngoài.
/// Đặt chỗ dùng UPDATE có điều kiện nguyên tử → không oversell khi có nhiều request đồng thời.
/// </summary>
public class InventoryService(ShopDbContext db)
{
    public async Task ReserveAsync(Guid variantId, int qty, string reference, CancellationToken ct)
    {
        var affected = await db.Inventory
            .Where(i => i.VariantId == variantId && i.OnHand - i.Reserved >= qty)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Reserved, i => i.Reserved + qty), ct);
        if (affected == 0)
            throw DomainException.Conflict("Sản phẩm không đủ tồn kho.", "insufficient_stock");
        Record(variantId, InventoryMovementType.Reserve, -qty, reference);
    }

    public async Task ReleaseAsync(Guid variantId, int qty, string reference, CancellationToken ct)
    {
        await db.Inventory.Where(i => i.VariantId == variantId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Reserved, i => i.Reserved - qty), ct);
        Record(variantId, InventoryMovementType.Release, qty, reference);
    }

    /// <summary>Xuất kho khi giao hàng: trừ OnHand và giải phóng phần đã đặt chỗ.</summary>
    public async Task ShipAsync(Guid variantId, int qty, string reference, CancellationToken ct)
    {
        await db.Inventory.Where(i => i.VariantId == variantId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.OnHand, i => i.OnHand - qty)
                .SetProperty(i => i.Reserved, i => i.Reserved - qty), ct);
        Record(variantId, InventoryMovementType.Ship, -qty, reference);
    }

    public async Task SetOnHandAsync(Guid variantId, int onHand, string reference, CancellationToken ct)
    {
        var item = await db.Inventory.FirstOrDefaultAsync(i => i.VariantId == variantId, ct);
        if (item is null)
        {
            db.Inventory.Add(new InventoryItem { VariantId = variantId, OnHand = onHand });
            Record(variantId, InventoryMovementType.Receive, onHand, reference);
        }
        else if (item.OnHand != onHand)
        {
            Record(variantId, InventoryMovementType.Adjust, onHand - item.OnHand, reference);
            item.OnHand = onHand;
        }
        await db.SaveChangesAsync(ct);
    }

    private void Record(Guid variantId, InventoryMovementType type, int qty, string reference)
    {
        db.InventoryMovements.Add(new InventoryMovement { VariantId = variantId, Type = type, Quantity = qty, Reference = reference });
        db.Enqueue("InventoryChanged", new { VariantId = variantId, Type = type.ToString(), Quantity = qty, Reference = reference });
    }
}

public record StockAdjustRequest([Range(0, 1_000_000)] int OnHand, [MaxLength(100)] string? Reason);

[ApiController]
[Authorize(Roles = Roles.Admin)]
[Route("api/v1/admin/inventory")]
public class AdminInventoryController(ShopDbContext db, InventoryService inventory) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] bool lowStockOnly = false, [FromQuery] int threshold = 5, CancellationToken ct = default)
    {
        var q = db.Variants.AsNoTracking().Where(v => v.Inventory != null);
        if (lowStockOnly) q = q.Where(v => v.Inventory!.OnHand - v.Inventory.Reserved <= threshold);
        var items = await q.OrderBy(v => v.Sku).Take(500).Select(v => new
        {
            v.Id, v.Sku, Product = v.Product!.Name, Variant = v.Name, v.Inventory!.OnHand, v.Inventory.Reserved,
            Available = v.Inventory.OnHand - v.Inventory.Reserved,
        }).ToListAsync(ct);
        return Ok(items);
    }

    [HttpPut("{variantId:guid}")]
    public async Task<IActionResult> Adjust(Guid variantId, StockAdjustRequest req, CancellationToken ct)
    {
        if (!await db.Variants.AnyAsync(v => v.Id == variantId, ct)) throw DomainException.NotFound("Variant");
        await inventory.SetOnHandAsync(variantId, req.OnHand, $"adjust:{req.Reason ?? "manual"}", ct);
        return NoContent();
    }

    [HttpGet("{variantId:guid}/movements")]
    public async Task<List<InventoryMovement>> Movements(Guid variantId, CancellationToken ct) =>
        await db.InventoryMovements.AsNoTracking().Where(m => m.VariantId == variantId)
            .OrderByDescending(m => m.Id).Take(200).ToListAsync(ct);
}
