using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MyShop.Api.Common;
using MyShop.Api.Domain;
using MyShop.Api.Infrastructure;
using MyShop.Api.Modules.Orders;

namespace MyShop.Api.Modules.Payments;

public class WebhookOptions
{
    /// <summary>Secret dùng ký HMAC cho từng nguồn webhook (đặt qua secret store).</summary>
    public Dictionary<string, string> Secrets { get; set; } = new();
    public int ToleranceSeconds { get; set; } = 300;
    public bool EnableMockPay { get; set; } = true;
}

/// <summary>
/// Chữ ký webhook dạng "t=&lt;unix&gt;,v1=&lt;hex hmac_sha256(secret, t + "." + body)&gt;" —
/// có timestamp để chống replay, so sánh constant-time.
/// </summary>
public static class WebhookSignature
{
    public const string Header = "X-MyShop-Signature";

    public static string Sign(string secret, string body, long timestamp)
    {
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{body}"));
        return $"t={timestamp},v1={Convert.ToHexString(mac).ToLowerInvariant()}";
    }

    public static bool Verify(string secret, string body, string? header, DateTimeOffset now, int toleranceSeconds)
    {
        if (string.IsNullOrEmpty(header)) return false;
        var parts = header.Split(',').Select(p => p.Split('=', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0], p => p[1]);
        if (!parts.TryGetValue("t", out var ts) || !parts.TryGetValue("v1", out var sig)) return false;
        if (!long.TryParse(ts, NumberStyles.None, CultureInfo.InvariantCulture, out var t)) return false;
        if (Math.Abs(now.ToUnixTimeSeconds() - t) > toleranceSeconds) return false;
        var expected = Sign(secret, body, t);
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes($"t={ts},v1={sig}"));
    }
}

public record PaymentWebhook(string EventId, string Type, Guid PaymentId, string? ProviderReference, decimal Amount);
public record ShippingWebhook(string EventId, string TrackingNumber, string Status);

public class WebhookProcessor(ShopDbContext db, OrderService orders, ILogger<WebhookProcessor> log)
{
    /// <returns>false nếu sự kiện đã xử lý trước đó (idempotent).</returns>
    public async Task<bool> HandlePaymentAsync(string source, PaymentWebhook evt, string rawBody, CancellationToken ct)
    {
        if (!await RegisterEventAsync(source, evt.EventId, rawBody, ct)) return false;

        var payment = await db.Payments.FirstOrDefaultAsync(p => p.Id == evt.PaymentId, ct) ?? throw DomainException.NotFound("Payment");
        if (payment.Status != PaymentStatus.Pending)
        {
            log.LogInformation("Payment {Id} already {Status}, ignoring {Type}", payment.Id, payment.Status, evt.Type);
            return true;
        }
        payment.ProviderReference = evt.ProviderReference;
        payment.UpdatedAt = DateTime.UtcNow;

        switch (evt.Type)
        {
            case "payment.succeeded":
                if (evt.Amount != payment.Amount)
                    throw new DomainException("Số tiền thanh toán không khớp.", 400, "amount_mismatch");
                payment.Status = PaymentStatus.Succeeded;
                await db.SaveChangesAsync(ct);
                await orders.ConfirmAsync(payment.OrderId, $"Thanh toán thành công ({source})", ct);
                break;
            case "payment.failed":
                payment.Status = PaymentStatus.Failed;
                await db.SaveChangesAsync(ct);
                await orders.CancelAsync(payment.OrderId, $"Thanh toán thất bại ({source})", ct);
                break;
            default:
                throw new DomainException($"Loại sự kiện không hỗ trợ: {evt.Type}");
        }
        return true;
    }

    public async Task<bool> HandleShippingAsync(string source, ShippingWebhook evt, string rawBody, CancellationToken ct)
    {
        if (!await RegisterEventAsync(source, evt.EventId, rawBody, ct)) return false;
        var shipment = await db.Shipments.FirstOrDefaultAsync(s => s.TrackingNumber == evt.TrackingNumber, ct)
                       ?? throw DomainException.NotFound("Vận đơn");
        switch (evt.Status)
        {
            case "in_transit":
                shipment.Status = ShipmentStatus.InTransit;
                await db.SaveChangesAsync(ct);
                break;
            case "delivered":
                await orders.CompleteAsync(shipment.OrderId, $"Đã giao ({source})", ct);
                break;
            case "failed":
                shipment.Status = ShipmentStatus.Failed;
                await db.SaveChangesAsync(ct);
                break;
            default:
                throw new DomainException($"Trạng thái không hỗ trợ: {evt.Status}");
        }
        return true;
    }

    private async Task<bool> RegisterEventAsync(string source, string eventId, string body, CancellationToken ct)
    {
        if (await db.WebhookEvents.AnyAsync(e => e.Source == source && e.EventId == eventId, ct)) return false;
        db.WebhookEvents.Add(new WebhookEvent { Source = source, EventId = eventId, Payload = body });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            // Hai request trùng đến đồng thời → unique index chặn bản thứ hai.
            db.ChangeTracker.Clear();
            return false;
        }
        return true;
    }
}

[ApiController]
[Route("api/v1")]
public class PaymentsController(ShopDbContext db, WebhookProcessor processor, IOptions<WebhookOptions> options, TimeProvider clock)
    : ControllerBase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Thông tin tối thiểu cho trang thanh toán (không lộ dữ liệu cá nhân).</summary>
    [HttpGet("payments/{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var p = await db.Payments.AsNoTracking().Where(x => x.Id == id)
            .Join(db.Orders, x => x.OrderId, o => o.Id, (x, o) => new
            {
                x.Id, x.Status, x.Method, x.Amount, OrderId = o.Id, o.Number, o.Currency, o.ExchangeRate,
            }).FirstOrDefaultAsync(ct) ?? throw DomainException.NotFound("Payment");
        return Ok(new
        {
            p.Id, p.Status, p.Method, p.OrderId, OrderNumber = p.Number, AmountVnd = p.Amount, p.Currency,
            Amount = CurrencyService.Convert(p.Amount, p.ExchangeRate, p.Currency),
        });
    }

    /// <summary>
    /// Mô phỏng cổng thanh toán (chỉ dev/demo): ký webhook bằng secret của "mockpay" và gửi vào cùng luồng xử lý
    /// như webhook thật. Ở production thay bằng redirect sang VNPay/MoMo/Stripe…
    /// </summary>
    [HttpPost("payments/mockpay/{id:guid}/simulate")]
    public async Task<IActionResult> Simulate(Guid id, [FromQuery] bool success, CancellationToken ct)
    {
        if (!options.Value.EnableMockPay) return NotFound();
        var payment = await db.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw DomainException.NotFound("Payment");
        var evt = new PaymentWebhook($"evt_{Guid.NewGuid():N}", success ? "payment.succeeded" : "payment.failed", id,
            $"mock_{Guid.NewGuid():N}"[..20], payment.Amount);
        var body = JsonSerializer.Serialize(evt, Json);
        await processor.HandlePaymentAsync("mockpay", evt, body, ct);
        return Ok(new { status = success ? "succeeded" : "failed" });
    }

    [HttpPost("webhooks/payments/{source}")]
    public async Task<IActionResult> PaymentWebhook(string source, CancellationToken ct)
    {
        var (ok, body) = await VerifyAsync(source, ct);
        if (!ok) return Unauthorized();
        var evt = JsonSerializer.Deserialize<PaymentWebhook>(body, Json) ?? throw new DomainException("Payload không hợp lệ.");
        var processed = await processor.HandlePaymentAsync(source, evt, body, ct);
        return Ok(new { received = true, duplicate = !processed });
    }

    [HttpPost("webhooks/shipping/{source}")]
    public async Task<IActionResult> ShippingWebhook(string source, CancellationToken ct)
    {
        var (ok, body) = await VerifyAsync(source, ct);
        if (!ok) return Unauthorized();
        var evt = JsonSerializer.Deserialize<ShippingWebhook>(body, Json) ?? throw new DomainException("Payload không hợp lệ.");
        var processed = await processor.HandleShippingAsync(source, evt, body, ct);
        return Ok(new { received = true, duplicate = !processed });
    }

    private async Task<(bool, string)> VerifyAsync(string source, CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync(ct);
        if (!options.Value.Secrets.TryGetValue(source, out var secret) || string.IsNullOrEmpty(secret)) return (false, body);
        var ok = WebhookSignature.Verify(secret, body, Request.Headers[WebhookSignature.Header], clock.GetUtcNow(), options.Value.ToleranceSeconds);
        return (ok, body);
    }
}
