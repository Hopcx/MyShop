using MyShop.Api.Common;
using MyShop.Api.Domain;

namespace MyShop.Api.Modules.Orders;

/// <summary>Máy trạng thái đơn hàng 4 bước: Created → Confirmed → Shipping → Completed, có thể Cancelled trước khi giao.</summary>
public static class OrderStateMachine
{
    private static readonly Dictionary<OrderStatus, OrderStatus[]> Allowed = new()
    {
        [OrderStatus.Created] = [OrderStatus.Confirmed, OrderStatus.Cancelled],
        [OrderStatus.Confirmed] = [OrderStatus.Shipping, OrderStatus.Cancelled],
        [OrderStatus.Shipping] = [OrderStatus.Completed],
        [OrderStatus.Completed] = [],
        [OrderStatus.Cancelled] = [],
    };

    public static bool CanTransition(OrderStatus from, OrderStatus to) => Allowed[from].Contains(to);

    public static void Transition(Order order, OrderStatus to, string note = "")
    {
        if (!CanTransition(order.Status, to))
            throw DomainException.Conflict($"Không thể chuyển đơn từ {order.Status} sang {to}.", "invalid_transition");
        order.History.Add(new OrderStatusHistory { OrderId = order.Id, From = order.Status, To = to, Note = note });
        order.Status = to;
        order.UpdatedAt = DateTime.UtcNow;
    }
}

public record PriceBreakdown(decimal Subtotal, decimal Discount, decimal ShippingFee, decimal Total, string? AppliedPromotion);

public class PricingOptions
{
    public decimal FreeShippingThreshold { get; set; } = 500_000m;
    public decimal FlatShippingFee { get; set; } = 30_000m;
}

/// <summary>Tính giá thuần (pure) — dễ unit test; mọi số tiền theo VND.</summary>
public static class Pricing
{
    public static PriceBreakdown Calculate(decimal subtotal, Promotion? promo, PricingOptions opt, DateTime now)
    {
        decimal discount = 0;
        string? applied = null;
        if (promo is not null)
        {
            ValidatePromotion(promo, subtotal, now);
            discount = promo.Type == PromotionType.Percent ? Math.Round(subtotal * promo.Value / 100m, 0) : promo.Value;
            if (promo.MaxDiscount is { } cap) discount = Math.Min(discount, cap);
            discount = Math.Min(discount, subtotal);
            applied = promo.Code;
        }
        var afterDiscount = subtotal - discount;
        var shipping = afterDiscount >= opt.FreeShippingThreshold || subtotal == 0 ? 0 : opt.FlatShippingFee;
        return new PriceBreakdown(subtotal, discount, shipping, afterDiscount + shipping, applied);
    }

    public static void ValidatePromotion(Promotion p, decimal subtotal, DateTime now)
    {
        if (!p.IsActive || now < p.StartsAt || (p.EndsAt is { } end && now > end))
            throw new DomainException("Mã khuyến mãi không còn hiệu lực.", 400, "promotion_expired");
        if (p.UsageLimit is { } limit && p.UsedCount >= limit)
            throw new DomainException("Mã khuyến mãi đã hết lượt sử dụng.", 400, "promotion_exhausted");
        if (subtotal < p.MinSubtotal)
            throw new DomainException($"Đơn tối thiểu {p.MinSubtotal:N0}đ để dùng mã này.", 400, "promotion_min_subtotal");
    }
}
