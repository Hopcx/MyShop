using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyShop.Api.Domain;
using MyShop.Api.Infrastructure;

namespace MyShop.Api.Modules.Reporting;

public record DailySales(DateOnly Date, int Orders, decimal Revenue);
public record TopProduct(Guid ProductId, string Name, int Quantity, decimal Revenue);

/// <summary>KPI: doanh thu, số đơn, AOV, tỉ lệ chuyển đổi (đơn / giỏ tạo mới), LTV trung bình, top sản phẩm.</summary>
public record SalesSummary(DateTime From, DateTime To, int Orders, int CancelledOrders, decimal Revenue, decimal AverageOrderValue,
    int CartsCreated, double ConversionRate, decimal AverageCustomerLifetimeValue, IReadOnlyList<DailySales> Daily,
    IReadOnlyList<TopProduct> TopProducts, IReadOnlyDictionary<string, int> OrdersByStatus);

[ApiController]
[Authorize(Roles = Roles.Admin)]
[Route("api/v1/admin/reports")]
public class ReportsController(ShopDbContext db) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<SalesSummary> Summary([FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
    {
        var end = (to ?? DateTime.UtcNow).ToUniversalTime();
        var start = (from ?? end.AddDays(-30)).ToUniversalTime();

        var orders = db.Orders.AsNoTracking().Where(o => o.CreatedAt >= start && o.CreatedAt < end);
        var byStatus = await orders.GroupBy(o => o.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var valid = orders.Where(o => o.Status != OrderStatus.Cancelled);

        // Tổng hợp theo ngày phía client cho tương thích SQLite/SQL Server; khối lượng lớn → chuyển sang bảng tổng hợp/ETL.
        var rows = await valid.Select(o => new { o.CreatedAt, o.Total, o.Email }).ToListAsync(ct);
        var daily = rows.GroupBy(r => DateOnly.FromDateTime(r.CreatedAt))
            .Select(g => new DailySales(g.Key, g.Count(), g.Sum(x => x.Total))).OrderBy(d => d.Date).ToList();

        var revenue = rows.Sum(r => r.Total);
        var count = rows.Count;
        var carts = await db.Carts.AsNoTracking().CountAsync(c => c.CreatedAt >= start && c.CreatedAt < end, ct);

        var lifetime = await db.Orders.AsNoTracking().Where(o => o.Status == OrderStatus.Completed)
            .Select(o => new { o.Email, o.Total }).ToListAsync(ct);
        var ltv = lifetime.Count == 0 ? 0 : lifetime.GroupBy(x => x.Email).Average(g => g.Sum(x => x.Total));

        var top = await db.OrderItems.AsNoTracking()
            .Join(valid, i => i.OrderId, o => o.Id, (i, o) => i)
            .GroupBy(i => new { i.ProductId, i.ProductName })
            .Select(g => new { g.Key.ProductId, g.Key.ProductName, Qty = g.Sum(i => i.Quantity), Revenue = g.Sum(i => i.UnitPrice * i.Quantity) })
            .OrderByDescending(x => x.Qty).Take(10).ToListAsync(ct);

        return new SalesSummary(start, end, count, byStatus.Where(s => s.Key == OrderStatus.Cancelled).Sum(s => s.Count), revenue,
            count == 0 ? 0 : Math.Round(revenue / count, 0), carts, carts == 0 ? 0 : Math.Round((double)count / carts, 4),
            Math.Round(ltv, 0), daily, top.Select(t => new TopProduct(t.ProductId, t.ProductName, t.Qty, t.Revenue)).ToList(),
            byStatus.ToDictionary(s => s.Key.ToString(), s => s.Count));
    }
}
