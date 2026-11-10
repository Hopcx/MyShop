using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyShop.Api.Common;
using MyShop.Api.Domain;
using MyShop.Api.Infrastructure;
using CartEntity = MyShop.Api.Domain.Cart;

namespace MyShop.Api.Modules.Cart;

public record AddItemRequest(Guid VariantId, [Range(1, 99)] int Quantity);
public record UpdateItemRequest([Range(0, 99)] int Quantity);

public record CartLineDto(Guid Id, Guid VariantId, Guid ProductId, string ProductSlug, string ProductName, string VariantName,
    string Sku, string? Image, decimal UnitPrice, int Quantity, decimal LineTotal, int Available);

public record CartDto(Guid Id, string Currency, IReadOnlyList<CartLineDto> Items, int ItemCount, decimal Subtotal);

/// <summary>
/// Giỏ hàng hỗ trợ cả khách vãng lai (guest — client giữ cartId) và người dùng đăng nhập (gắn UserId, merge khi login).
/// </summary>
[ApiController]
[Route("api/v1/carts")]
public class CartController(ShopDbContext db, CurrencyService fx) : ControllerBase
{
    private const int MaxLines = 50;

    [HttpPost]
    public async Task<ActionResult<CartDto>> Create([FromQuery] string? currency, CancellationToken ct)
    {
        var uid = User.UserId();
        if (uid is not null)
        {
            var existing = await db.Carts.FirstOrDefaultAsync(c => c.UserId == uid && !c.CheckedOut, ct);
            if (existing is not null) return await GetAsync(existing.Id, currency, ct);
        }
        var cart = new CartEntity { UserId = uid };
        db.Carts.Add(cart);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = cart.Id }, await GetAsync(cart.Id, currency, ct));
    }

    [HttpGet("{id:guid}")]
    public Task<CartDto> Get(Guid id, [FromQuery] string? currency, CancellationToken ct) => GetAsync(id, currency, ct);

    [HttpPost("{id:guid}/items")]
    public async Task<CartDto> AddItem(Guid id, AddItemRequest req, [FromQuery] string? currency, CancellationToken ct)
    {
        var cart = await LoadAsync(id, ct);
        var variant = await db.Variants.Include(v => v.Inventory).Include(v => v.Product)
                          .FirstOrDefaultAsync(v => v.Id == req.VariantId && v.Product!.IsActive, ct)
                      ?? throw DomainException.NotFound("Sản phẩm");
        var line = cart.Items.FirstOrDefault(i => i.VariantId == req.VariantId);
        var newQty = (line?.Quantity ?? 0) + req.Quantity;
        EnsureAvailable(variant, newQty);
        if (line is null)
        {
            if (cart.Items.Count >= MaxLines) throw new DomainException("Giỏ hàng đã đầy.");
            db.CartItems.Add(new CartItem { CartId = cart.Id, VariantId = variant.Id, Quantity = req.Quantity });
        }
        else line.Quantity = newQty;
        cart.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, currency, ct);
    }

    [HttpPatch("{id:guid}/items/{itemId:guid}")]
    public async Task<CartDto> UpdateItem(Guid id, Guid itemId, UpdateItemRequest req, [FromQuery] string? currency, CancellationToken ct)
    {
        var cart = await LoadAsync(id, ct);
        var line = cart.Items.FirstOrDefault(i => i.Id == itemId) ?? throw DomainException.NotFound("Dòng giỏ hàng");
        if (req.Quantity == 0) db.CartItems.Remove(line);
        else
        {
            var variant = await db.Variants.Include(v => v.Inventory).FirstAsync(v => v.Id == line.VariantId, ct);
            EnsureAvailable(variant, req.Quantity);
            line.Quantity = req.Quantity;
        }
        cart.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, currency, ct);
    }

    [HttpDelete("{id:guid}/items/{itemId:guid}")]
    public async Task<CartDto> RemoveItem(Guid id, Guid itemId, [FromQuery] string? currency, CancellationToken ct)
    {
        await LoadAsync(id, ct);
        await db.CartItems.Where(i => i.CartId == id && i.Id == itemId).ExecuteDeleteAsync(ct);
        return await GetAsync(id, currency, ct);
    }

    /// <summary>Gộp giỏ guest vào giỏ của user sau khi đăng nhập.</summary>
    [HttpPost("{id:guid}/merge")]
    [Microsoft.AspNetCore.Authorization.Authorize]
    public async Task<CartDto> Merge(Guid id, [FromQuery] string? currency, CancellationToken ct)
    {
        var uid = User.RequiredUserId();
        var guest = await LoadAsync(id, ct);
        var target = await db.Carts.Include(c => c.Items).FirstOrDefaultAsync(c => c.UserId == uid && !c.CheckedOut && c.Id != id, ct);
        if (target is null)
        {
            guest.UserId = uid;
            await db.SaveChangesAsync(ct);
            return await GetAsync(guest.Id, currency, ct);
        }
        foreach (var item in guest.Items)
        {
            var existing = target.Items.FirstOrDefault(i => i.VariantId == item.VariantId);
            if (existing is null) db.CartItems.Add(new CartItem { CartId = target.Id, VariantId = item.VariantId, Quantity = item.Quantity });
            else existing.Quantity = Math.Min(99, existing.Quantity + item.Quantity);
        }
        db.Carts.Remove(guest);
        await db.SaveChangesAsync(ct);
        return await GetAsync(target.Id, currency, ct);
    }

    private async Task<CartEntity> LoadAsync(Guid id, CancellationToken ct)
    {
        var cart = await db.Carts.Include(c => c.Items).FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw DomainException.NotFound("Giỏ hàng");
        if (cart.CheckedOut) throw DomainException.Conflict("Giỏ hàng đã được thanh toán.", "cart_checked_out");
        // Giỏ gắn với user thì chỉ chính user đó được thao tác.
        if (cart.UserId is not null && cart.UserId != User.UserId()) throw DomainException.NotFound("Giỏ hàng");
        return cart;
    }

    private static void EnsureAvailable(ProductVariant v, int qty)
    {
        var available = v.Inventory?.Available ?? 0;
        if (qty > available)
            throw DomainException.Conflict($"Chỉ còn {Math.Max(0, available)} sản phẩm '{v.Name}'.", "insufficient_stock");
    }

    private async Task<CartDto> GetAsync(Guid id, string? currency, CancellationToken ct)
    {
        var code = (currency ?? CurrencyService.Base).ToUpperInvariant();
        var rate = await fx.GetRateAsync(code, ct);
        var cart = await db.Carts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw DomainException.NotFound("Giỏ hàng");
        if (cart.UserId is not null && cart.UserId != User.UserId()) throw DomainException.NotFound("Giỏ hàng");

        var lines = await db.CartItems.AsNoTracking().Where(i => i.CartId == id).OrderBy(i => i.Id)
            .Select(i => new
            {
                i.Id, i.VariantId, i.Variant!.ProductId, Slug = i.Variant.Product!.Slug, ProductName = i.Variant.Product.Name,
                VariantName = i.Variant.Name, i.Variant.Sku, i.Variant.Price, i.Quantity,
                Image = i.Variant.Product.Images.OrderBy(x => x.SortOrder).Select(x => x.Url).FirstOrDefault(),
                Available = i.Variant.Inventory == null ? 0 : i.Variant.Inventory.OnHand - i.Variant.Inventory.Reserved,
            }).ToListAsync(ct);

        var items = lines.Select(l =>
        {
            var unit = CurrencyService.Convert(l.Price, rate, code);
            return new CartLineDto(l.Id, l.VariantId, l.ProductId, l.Slug, l.ProductName, l.VariantName, l.Sku, l.Image, unit,
                l.Quantity, unit * l.Quantity, Math.Max(0, l.Available));
        }).ToList();
        return new CartDto(cart.Id, code, items, items.Sum(i => i.Quantity), items.Sum(i => i.LineTotal));
    }
}
