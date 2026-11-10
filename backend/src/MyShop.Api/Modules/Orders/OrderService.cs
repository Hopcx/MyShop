using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MyShop.Api.Common;
using MyShop.Api.Domain;
using MyShop.Api.Infrastructure;

namespace MyShop.Api.Modules.Orders;

public record AddressInput(
    [Required, MaxLength(200)] string FullName,
    [Required, RegularExpression(@"^\+?[0-9 ]{8,15}$")] string Phone,
    [Required, MaxLength(300)] string Line1,
    [MaxLength(100)] string? Ward,
    [MaxLength(100)] string? District,
    [Required, MaxLength(100)] string City,
    [MaxLength(2)] string? Country);

public record CheckoutRequest(
    Guid CartId,
    [Required, EmailAddress, MaxLength(256)] string Email,
    [Required] AddressInput ShippingAddress,
    PaymentMethod PaymentMethod,
    [MaxLength(50)] string? PromotionCode,
    [RegularExpression("^[A-Za-z]{3}$")] string? Currency);

public record QuoteRequest(Guid CartId, [MaxLength(50)] string? PromotionCode, [RegularExpression("^[A-Za-z]{3}$")] string? Currency);

public record QuoteDto(string Currency, decimal Subtotal, decimal Discount, decimal ShippingFee, decimal Total, string? AppliedPromotion);

public record OrderItemDto(Guid ProductId, Guid VariantId, string Sku, string ProductName, string VariantName, decimal UnitPrice, int Quantity, decimal LineTotal);
public record OrderHistoryDto(OrderStatus From, OrderStatus To, string Note, DateTime At);

public record OrderDto(Guid Id, string Number, OrderStatus Status, PaymentMethod PaymentMethod, PaymentStatus? PaymentStatus,
    string Email, string Currency, decimal Subtotal, decimal Discount, decimal ShippingFee, decimal Total,
    string? PromotionCode, ShippingAddress ShippingAddress, DateTime CreatedAt,
    IReadOnlyList<OrderItemDto> Items, IReadOnlyList<OrderHistoryDto> History, string? TrackingNumber);

public record CheckoutResult(OrderDto Order, Guid PaymentId, string? PaymentUrl);

public class OrderService(ShopDbContext db, InventoryService inventory, IOptions<PricingOptions> pricing, TimeProvider clock,
    IConfiguration config, ILogger<OrderService> log)
{
    public async Task<QuoteDto> QuoteAsync(QuoteRequest req, Guid? userId, CurrencyService fx, CancellationToken ct)
    {
        var (cart, subtotal) = await LoadCartAsync(req.CartId, userId, ct);
        _ = cart;
        var promo = await FindPromotionAsync(req.PromotionCode, ct);
        var price = Pricing.Calculate(subtotal, promo, pricing.Value, clock.GetUtcNow().UtcDateTime);
        var code = (req.Currency ?? CurrencyService.Base).ToUpperInvariant();
        var rate = await fx.GetRateAsync(code, ct);
        return new QuoteDto(code, CurrencyService.Convert(price.Subtotal, rate, code), CurrencyService.Convert(price.Discount, rate, code),
            CurrencyService.Convert(price.ShippingFee, rate, code), CurrencyService.Convert(price.Total, rate, code), price.AppliedPromotion);
    }

    public async Task<CheckoutResult> CheckoutAsync(CheckoutRequest req, Guid? userId, CurrencyService fx, CancellationToken ct)
    {
        var currency = (req.Currency ?? CurrencyService.Base).ToUpperInvariant();
        var rate = await fx.GetRateAsync(currency, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var (cart, subtotal) = await LoadCartAsync(req.CartId, userId, ct);
        var promo = await FindPromotionAsync(req.PromotionCode, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var price = Pricing.Calculate(subtotal, promo, pricing.Value, now);

        var order = new Order
        {
            Number = NewOrderNumber(now), UserId = userId, Email = req.Email.Trim().ToLowerInvariant(),
            PaymentMethod = req.PaymentMethod, Currency = currency, ExchangeRate = rate,
            Subtotal = price.Subtotal, Discount = price.Discount, ShippingFee = price.ShippingFee, Total = price.Total,
            PromotionCode = price.AppliedPromotion,
            ShippingAddress = new ShippingAddress
            {
                FullName = req.ShippingAddress.FullName, Phone = req.ShippingAddress.Phone, Line1 = req.ShippingAddress.Line1,
                Ward = req.ShippingAddress.Ward ?? "", District = req.ShippingAddress.District ?? "", City = req.ShippingAddress.City,
                Country = req.ShippingAddress.Country ?? "VN",
            },
        };

        foreach (var line in cart.Items)
        {
            var v = line.Variant!;
            await inventory.ReserveAsync(v.Id, line.Quantity, $"order:{order.Number}", ct);
            order.Items.Add(new OrderItem
            {
                OrderId = order.Id, ProductId = v.ProductId, VariantId = v.Id, Sku = v.Sku, ProductName = v.Product!.Name,
                VariantName = v.Name, UnitPrice = v.Price, Quantity = line.Quantity,
            });
        }

        if (promo is not null)
        {
            // Tăng lượt dùng nguyên tử, tránh vượt UsageLimit khi nhiều đơn đồng thời.
            var ok = await db.Promotions.Where(p => p.Id == promo.Id && (p.UsageLimit == null || p.UsedCount < p.UsageLimit))
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.UsedCount, p => p.UsedCount + 1), ct);
            if (ok == 0) throw new DomainException("Mã khuyến mãi đã hết lượt sử dụng.", 400, "promotion_exhausted");
        }

        cart.CheckedOut = true;
        db.Orders.Add(order);
        var payment = new Payment { OrderId = order.Id, Method = req.PaymentMethod, Amount = order.Total };
        db.Payments.Add(payment);
        db.Enqueue("OrderCreated", new { OrderId = order.Id, order.Number, order.Email, order.UserId, order.Total });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        log.LogInformation("Order {Number} created, total {Total} VND, method {Method}", order.Number, order.Total, order.PaymentMethod);

        string? paymentUrl = req.PaymentMethod == PaymentMethod.MockPay
            ? $"{config["App:FrontendUrl"]?.TrimEnd('/')}/checkout/pay/{payment.Id}"
            : null;
        return new CheckoutResult(await GetDtoAsync(order.Id, ct), payment.Id, paymentUrl);
    }

    public async Task ConfirmAsync(Guid orderId, string note, CancellationToken ct)
    {
        var order = await LoadOrderAsync(orderId, ct);
        OrderStateMachine.Transition(order, OrderStatus.Confirmed, note);
        db.Enqueue("OrderStatusChanged", Event(order));
        await db.SaveChangesAsync(ct);
    }

    public async Task CancelAsync(Guid orderId, string reason, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var order = await LoadOrderAsync(orderId, ct);
        OrderStateMachine.Transition(order, OrderStatus.Cancelled, reason);
        foreach (var item in order.Items)
            await inventory.ReleaseAsync(item.VariantId, item.Quantity, $"cancel:{order.Number}", ct);

        var payment = await db.Payments.Where(p => p.OrderId == orderId).OrderByDescending(p => p.CreatedAt).FirstOrDefaultAsync(ct);
        if (payment is not null)
        {
            // Đã thu tiền → hoàn tiền (gọi API refund của cổng thanh toán ở môi trường thật).
            payment.Status = payment.Status == PaymentStatus.Succeeded ? PaymentStatus.Refunded : PaymentStatus.Failed;
            payment.UpdatedAt = DateTime.UtcNow;
        }
        db.Enqueue("OrderStatusChanged", Event(order));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<Shipment> ShipAsync(Guid orderId, string carrier, string? trackingNumber, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var order = await LoadOrderAsync(orderId, ct);
        OrderStateMachine.Transition(order, OrderStatus.Shipping, $"Giao cho {carrier}");
        foreach (var item in order.Items)
            await inventory.ShipAsync(item.VariantId, item.Quantity, $"ship:{order.Number}", ct);
        var shipment = new Shipment
        {
            OrderId = order.Id, Carrier = carrier,
            TrackingNumber = trackingNumber ?? $"{carrier.ToUpperInvariant()[..Math.Min(3, carrier.Length)]}{RandomNumberGenerator.GetInt32(100_000_000, 999_999_999)}",
        };
        db.Shipments.Add(shipment);
        db.Enqueue("OrderStatusChanged", Event(order));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return shipment;
    }

    public async Task CompleteAsync(Guid orderId, string note, CancellationToken ct)
    {
        var order = await LoadOrderAsync(orderId, ct);
        OrderStateMachine.Transition(order, OrderStatus.Completed, note);
        var payment = await db.Payments.Where(p => p.OrderId == orderId).OrderByDescending(p => p.CreatedAt).FirstOrDefaultAsync(ct);
        if (payment is { Method: PaymentMethod.Cod, Status: PaymentStatus.Pending })
        {
            payment.Status = PaymentStatus.Succeeded; // COD: thu tiền khi giao thành công.
            payment.UpdatedAt = DateTime.UtcNow;
        }
        var shipment = await db.Shipments.FirstOrDefaultAsync(s => s.OrderId == orderId, ct);
        if (shipment is not null) shipment.Status = ShipmentStatus.Delivered;
        db.Enqueue("OrderStatusChanged", Event(order));
        await db.SaveChangesAsync(ct);
    }

    public async Task<OrderDto> GetDtoAsync(Guid orderId, CancellationToken ct)
    {
        var o = await db.Orders.AsNoTracking().AsSplitQuery().Include(x => x.Items).Include(x => x.History)
            .FirstOrDefaultAsync(x => x.Id == orderId, ct) ?? throw DomainException.NotFound("Đơn hàng");
        var payment = await db.Payments.AsNoTracking().Where(p => p.OrderId == orderId).OrderByDescending(p => p.CreatedAt)
            .Select(p => (PaymentStatus?)p.Status).FirstOrDefaultAsync(ct);
        var tracking = await db.Shipments.AsNoTracking().Where(s => s.OrderId == orderId).Select(s => s.TrackingNumber).FirstOrDefaultAsync(ct);
        return ToDto(o, payment, tracking);
    }

    public static OrderDto ToDto(Order o, PaymentStatus? payment, string? tracking)
    {
        decimal C(decimal v) => CurrencyService.Convert(v, o.ExchangeRate, o.Currency);
        return new OrderDto(o.Id, o.Number, o.Status, o.PaymentMethod, payment, o.Email, o.Currency, C(o.Subtotal), C(o.Discount),
            C(o.ShippingFee), C(o.Total), o.PromotionCode, o.ShippingAddress, o.CreatedAt,
            o.Items.Select(i => new OrderItemDto(i.ProductId, i.VariantId, i.Sku, i.ProductName, i.VariantName, C(i.UnitPrice), i.Quantity,
                C(i.UnitPrice) * i.Quantity)).ToList(),
            o.History.OrderBy(h => h.CreatedAt).Select(h => new OrderHistoryDto(h.From, h.To, h.Note, h.CreatedAt)).ToList(),
            tracking);
    }

    private async Task<(Domain.Cart Cart, decimal Subtotal)> LoadCartAsync(Guid cartId, Guid? userId, CancellationToken ct)
    {
        var cart = await db.Carts.Include(c => c.Items).ThenInclude(i => i.Variant).ThenInclude(v => v!.Product)
                       .FirstOrDefaultAsync(c => c.Id == cartId, ct) ?? throw DomainException.NotFound("Giỏ hàng");
        if (cart.UserId is not null && cart.UserId != userId) throw DomainException.NotFound("Giỏ hàng");
        if (cart.CheckedOut) throw DomainException.Conflict("Giỏ hàng đã được thanh toán.", "cart_checked_out");
        if (cart.Items.Count == 0) throw new DomainException("Giỏ hàng trống.", 400, "cart_empty");
        if (cart.Items.Any(i => i.Variant?.Product is not { IsActive: true }))
            throw DomainException.Conflict("Giỏ hàng có sản phẩm đã ngừng bán.", "product_unavailable");
        return (cart, cart.Items.Sum(i => i.Variant!.Price * i.Quantity));
    }

    private async Task<Promotion?> FindPromotionAsync(string? code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var normalized = code.Trim().ToUpperInvariant();
        return await db.Promotions.AsNoTracking().FirstOrDefaultAsync(p => p.Code == normalized, ct)
               ?? throw new DomainException("Mã khuyến mãi không tồn tại.", 400, "promotion_not_found");
    }

    private async Task<Order> LoadOrderAsync(Guid id, CancellationToken ct) =>
        await db.Orders.Include(o => o.Items).Include(o => o.History).FirstOrDefaultAsync(o => o.Id == id, ct)
        ?? throw DomainException.NotFound("Đơn hàng");

    private static object Event(Order o) => new { OrderId = o.Id, o.Number, o.Email, o.UserId, Status = o.Status.ToString() };

    private static string NewOrderNumber(DateTime now) => $"MS{now:yyMMdd}{RandomNumberGenerator.GetInt32(0, 1_000_000):D6}";
}
