using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyShop.Api.Common;
using MyShop.Api.Domain;
using MyShop.Api.Infrastructure;

namespace MyShop.Api.Modules.Auth;

public record AddressRequest(
    [Required, MaxLength(200)] string FullName,
    [Required, RegularExpression(@"^\+?[0-9 ]{8,15}$")] string Phone,
    [Required, MaxLength(300)] string Line1,
    [MaxLength(100)] string? Ward,
    [MaxLength(100)] string? District,
    [Required, MaxLength(100)] string City,
    [MaxLength(2)] string? Country,
    bool IsDefault);

public record UpdateProfileRequest([Required, MaxLength(200)] string FullName, [RegularExpression("^(vi|en)$")] string? PreferredLocale);

[ApiController]
[Authorize]
[Route("api/v1/me")]
public class AccountController(ShopDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<UserDto>> Me(CancellationToken ct)
    {
        var user = await db.Users.FindAsync([User.RequiredUserId()], ct) ?? throw DomainException.NotFound("Người dùng");
        return TokenService.ToDto(user);
    }

    [HttpPut]
    public async Task<ActionResult<UserDto>> Update(UpdateProfileRequest req, CancellationToken ct)
    {
        var user = await db.Users.FindAsync([User.RequiredUserId()], ct) ?? throw DomainException.NotFound("Người dùng");
        user.FullName = req.FullName.Trim();
        if (req.PreferredLocale is not null) user.PreferredLocale = req.PreferredLocale;
        await db.SaveChangesAsync(ct);
        return TokenService.ToDto(user);
    }

    // ── Sổ địa chỉ ──
    [HttpGet("addresses")]
    public async Task<List<Address>> Addresses(CancellationToken ct)
    {
        var uid = User.RequiredUserId();
        return await db.Addresses.AsNoTracking().Where(a => a.UserId == uid).OrderByDescending(a => a.IsDefault).ToListAsync(ct);
    }

    [HttpPost("addresses")]
    public async Task<ActionResult<Address>> AddAddress(AddressRequest req, CancellationToken ct)
    {
        var uid = User.RequiredUserId();
        var isFirst = !await db.Addresses.AnyAsync(a => a.UserId == uid, ct);
        if (req.IsDefault && !isFirst)
            await db.Addresses.Where(a => a.UserId == uid).ExecuteUpdateAsync(s => s.SetProperty(a => a.IsDefault, false), ct);
        var address = new Address
        {
            UserId = uid, FullName = req.FullName, Phone = req.Phone, Line1 = req.Line1, Ward = req.Ward ?? "",
            District = req.District ?? "", City = req.City, Country = req.Country ?? "VN", IsDefault = req.IsDefault || isFirst,
        };
        db.Addresses.Add(address);
        await db.SaveChangesAsync(ct);
        return Created($"/api/v1/me/addresses/{address.Id}", address);
    }

    [HttpDelete("addresses/{id:guid}")]
    public async Task<IActionResult> DeleteAddress(Guid id, CancellationToken ct)
    {
        var uid = User.RequiredUserId();
        var n = await db.Addresses.Where(a => a.Id == id && a.UserId == uid).ExecuteDeleteAsync(ct);
        return n == 0 ? NotFound() : NoContent();
    }

    // ── Wishlist ──
    [HttpGet("wishlist")]
    public async Task<IActionResult> Wishlist(CancellationToken ct)
    {
        var uid = User.RequiredUserId();
        var items = await db.Wishlist.AsNoTracking().Where(w => w.UserId == uid)
            .OrderByDescending(w => w.CreatedAt)
            .Select(w => new { w.ProductId, w.Product!.Slug, w.Product.Name, w.Product.MinPrice,
                Image = w.Product.Images.OrderBy(i => i.SortOrder).Select(i => i.Url).FirstOrDefault() })
            .ToListAsync(ct);
        return Ok(items);
    }

    [HttpPut("wishlist/{productId:guid}")]
    public async Task<IActionResult> AddWishlist(Guid productId, CancellationToken ct)
    {
        var uid = User.RequiredUserId();
        if (!await db.Products.AnyAsync(p => p.Id == productId, ct)) throw DomainException.NotFound("Sản phẩm");
        if (!await db.Wishlist.AnyAsync(w => w.UserId == uid && w.ProductId == productId, ct))
        {
            db.Wishlist.Add(new WishlistItem { UserId = uid, ProductId = productId });
            await db.SaveChangesAsync(ct);
        }
        return NoContent();
    }

    [HttpDelete("wishlist/{productId:guid}")]
    public async Task<IActionResult> RemoveWishlist(Guid productId, CancellationToken ct)
    {
        var uid = User.RequiredUserId();
        await db.Wishlist.Where(w => w.UserId == uid && w.ProductId == productId).ExecuteDeleteAsync(ct);
        return NoContent();
    }

    // ── Thông báo web ──
    [HttpGet("notifications")]
    public async Task<List<Notification>> Notifications(CancellationToken ct)
    {
        var uid = User.RequiredUserId();
        return await db.Notifications.AsNoTracking().Where(n => n.UserId == uid)
            .OrderByDescending(n => n.CreatedAt).Take(50).ToListAsync(ct);
    }

    [HttpPost("notifications/read-all")]
    public async Task<IActionResult> ReadAll(CancellationToken ct)
    {
        var uid = User.RequiredUserId();
        await db.Notifications.Where(n => n.UserId == uid && !n.IsRead).ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true), ct);
        return NoContent();
    }
}
