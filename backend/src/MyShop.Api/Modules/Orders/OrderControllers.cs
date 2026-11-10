using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using MyShop.Api.Common;
using MyShop.Api.Domain;
using MyShop.Api.Infrastructure;

namespace MyShop.Api.Modules.Orders;

[ApiController]
[Route("api/v1")]
public class CheckoutController(OrderService orders, CurrencyService fx, ShopDbContext db) : ControllerBase
{
    [HttpPost("checkout/quote")]
    public Task<QuoteDto> Quote(QuoteRequest req, CancellationToken ct) => orders.QuoteAsync(req, User.UserId(), fx, ct);

    [HttpPost("checkout")]
    [EnableRateLimiting("checkout")]
    public async Task<ActionResult<CheckoutResult>> Checkout(CheckoutRequest req, CancellationToken ct)
    {
        var result = await orders.CheckoutAsync(req, User.UserId(), fx, ct);
        return Created($"/api/v1/orders/{result.Order.Id}", result);
    }

    /// <summary>Tra cứu đơn cho khách vãng lai: cần đúng cả mã đơn và email.</summary>
    [HttpGet("orders/lookup")]
    [EnableRateLimiting("auth")]
    public async Task<OrderDto> Lookup([FromQuery, Required] string number, [FromQuery, Required, EmailAddress] string email, CancellationToken ct)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var id = await db.Orders.Where(o => o.Number == number && o.Email == normalized).Select(o => (Guid?)o.Id).FirstOrDefaultAsync(ct)
                 ?? throw DomainException.NotFound("Đơn hàng");
        return await orders.GetDtoAsync(id, ct);
    }

    [HttpGet("orders")]
    [Authorize]
    public async Task<PagedResult<OrderDto>> MyOrders([FromQuery, Range(1, 1000)] int page = 1, CancellationToken ct = default)
    {
        const int size = 10;
        var uid = User.RequiredUserId();
        var q = db.Orders.AsNoTracking().Where(o => o.UserId == uid);
        var total = await q.CountAsync(ct);
        var list = await q.OrderByDescending(o => o.CreatedAt).Skip((page - 1) * size).Take(size)
            .Include(o => o.Items).Include(o => o.History).AsSplitQuery().ToListAsync(ct);
        var ids = list.Select(o => o.Id).ToList();
        var payments = await db.Payments.AsNoTracking().Where(p => ids.Contains(p.OrderId))
            .GroupBy(p => p.OrderId).Select(g => new { g.Key, Status = g.OrderByDescending(p => p.CreatedAt).Select(p => p.Status).First() })
            .ToDictionaryAsync(x => x.Key, x => x.Status, ct);
        var tracking = await db.Shipments.AsNoTracking().Where(s => ids.Contains(s.OrderId)).ToDictionaryAsync(s => s.OrderId, s => s.TrackingNumber, ct);
        return new PagedResult<OrderDto>(list.Select(o => OrderService.ToDto(o,
            payments.TryGetValue(o.Id, out var ps) ? ps : null, tracking.GetValueOrDefault(o.Id))).ToList(), page, size, total);
    }

    [HttpGet("orders/{id:guid}")]
    [Authorize]
    public async Task<OrderDto> Get(Guid id, CancellationToken ct)
    {
        var uid = User.RequiredUserId();
        var isAdmin = User.IsInRole(Roles.Admin);
        if (!isAdmin && !await db.Orders.AnyAsync(o => o.Id == id && o.UserId == uid, ct)) throw DomainException.NotFound("Đơn hàng");
        return await orders.GetDtoAsync(id, ct);
    }

    [HttpPost("orders/{id:guid}/cancel")]
    [Authorize]
    public async Task<OrderDto> Cancel(Guid id, CancellationToken ct)
    {
        var uid = User.RequiredUserId();
        var order = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id && o.UserId == uid, ct)
                    ?? throw DomainException.NotFound("Đơn hàng");
        // Khách chỉ được tự hủy khi đơn chưa xác nhận.
        if (order.Status != OrderStatus.Created) throw DomainException.Conflict("Đơn đã được xác nhận, vui lòng liên hệ CSKH.", "cannot_cancel");
        await orders.CancelAsync(id, "Khách hàng hủy", ct);
        return await orders.GetDtoAsync(id, ct);
    }

    [HttpPost("promotions/validate")]
    public async Task<IActionResult> ValidatePromotion([FromBody] QuoteRequest req, CancellationToken ct)
    {
        var quote = await orders.QuoteAsync(req, User.UserId(), fx, ct);
        return Ok(new { valid = quote.AppliedPromotion is not null, quote });
    }
}

public record ShipRequest([Required, MaxLength(50)] string Carrier, [MaxLength(64)] string? TrackingNumber);
public record NoteRequest([MaxLength(500)] string? Note);

[ApiController]
[Authorize(Roles = Roles.Admin)]
[Route("api/v1/admin/orders")]
public class AdminOrdersController(ShopDbContext db, OrderService orders) : ControllerBase
{
    [HttpGet]
    public async Task<PagedResult<OrderDto>> List([FromQuery] OrderStatus? status, [FromQuery] string? q,
        [FromQuery, Range(1, 10000)] int page = 1, [FromQuery, Range(1, 100)] int pageSize = 20, CancellationToken ct = default)
    {
        var query = db.Orders.AsNoTracking();
        if (status is not null) query = query.Where(o => o.Status == status);
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(o => o.Number.Contains(q) || o.Email.Contains(q));
        var total = await query.CountAsync(ct);
        var list = await query.OrderByDescending(o => o.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize)
            .Include(o => o.Items).Include(o => o.History).AsSplitQuery().ToListAsync(ct);
        return new PagedResult<OrderDto>(list.Select(o => OrderService.ToDto(o, null, null)).ToList(), page, pageSize, total);
    }

    [HttpGet("{id:guid}")]
    public Task<OrderDto> Get(Guid id, CancellationToken ct) => orders.GetDtoAsync(id, ct);

    [HttpPost("{id:guid}/confirm")]
    public async Task<OrderDto> Confirm(Guid id, NoteRequest req, CancellationToken ct)
    {
        await orders.ConfirmAsync(id, req.Note ?? "Admin xác nhận", ct);
        return await orders.GetDtoAsync(id, ct);
    }

    [HttpPost("{id:guid}/ship")]
    public async Task<OrderDto> Ship(Guid id, ShipRequest req, CancellationToken ct)
    {
        await orders.ShipAsync(id, req.Carrier, req.TrackingNumber, ct);
        return await orders.GetDtoAsync(id, ct);
    }

    [HttpPost("{id:guid}/complete")]
    public async Task<OrderDto> Complete(Guid id, NoteRequest req, CancellationToken ct)
    {
        await orders.CompleteAsync(id, req.Note ?? "Giao hàng thành công", ct);
        return await orders.GetDtoAsync(id, ct);
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<OrderDto> Cancel(Guid id, NoteRequest req, CancellationToken ct)
    {
        await orders.CancelAsync(id, req.Note ?? "Admin hủy", ct);
        return await orders.GetDtoAsync(id, ct);
    }
}

public record PromotionRequest([Required, RegularExpression("^[A-Z0-9_-]{3,50}$")] string Code, [MaxLength(300)] string? Description,
    PromotionType Type, [Range(0.01, 1_000_000_000)] decimal Value, decimal MinSubtotal, decimal? MaxDiscount,
    DateTime? StartsAt, DateTime? EndsAt, int? UsageLimit, bool IsActive = true);

[ApiController]
[Authorize(Roles = Roles.Admin)]
[Route("api/v1/admin/promotions")]
public class AdminPromotionsController(ShopDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<List<Promotion>> List(CancellationToken ct) => await db.Promotions.AsNoTracking().OrderBy(p => p.Code).ToListAsync(ct);

    [HttpPost]
    public async Task<IActionResult> Create(PromotionRequest req, CancellationToken ct)
    {
        if (req.Type == PromotionType.Percent && req.Value > 100) throw new DomainException("Phần trăm giảm tối đa 100.");
        if (await db.Promotions.AnyAsync(p => p.Code == req.Code, ct)) throw DomainException.Conflict("Mã đã tồn tại.");
        var p = new Promotion
        {
            Code = req.Code, Description = req.Description ?? "", Type = req.Type, Value = req.Value, MinSubtotal = req.MinSubtotal,
            MaxDiscount = req.MaxDiscount, StartsAt = req.StartsAt ?? DateTime.UtcNow, EndsAt = req.EndsAt, UsageLimit = req.UsageLimit,
            IsActive = req.IsActive,
        };
        db.Promotions.Add(p);
        await db.SaveChangesAsync(ct);
        return Created($"/api/v1/admin/promotions/{p.Id}", p);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        var n = await db.Promotions.Where(p => p.Id == id).ExecuteUpdateAsync(s => s.SetProperty(p => p.IsActive, false), ct);
        return n == 0 ? NotFound() : NoContent();
    }
}
