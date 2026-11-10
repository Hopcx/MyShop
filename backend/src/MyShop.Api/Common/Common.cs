using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MyShop.Api.Infrastructure;

namespace MyShop.Api.Common;

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(Total / (double)PageSize);
}

/// <summary>Lỗi nghiệp vụ → được middleware chuyển thành ProblemDetails với status tương ứng.</summary>
public class DomainException(string message, int statusCode = StatusCodes.Status400BadRequest, string code = "domain_error")
    : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;

    public static DomainException NotFound(string what) => new($"{what} không tồn tại.", 404, "not_found");
    public static DomainException Conflict(string msg, string code = "conflict") => new(msg, 409, code);
}

public static class ClaimsExtensions
{
    public static Guid? UserId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub"), out var id) ? id : null;

    public static Guid RequiredUserId(this ClaimsPrincipal user) =>
        user.UserId() ?? throw new DomainException("Chưa đăng nhập.", 401, "unauthorized");
}

public static class Locales
{
    public static readonly string[] Supported = ["vi", "en"];
    public const string Default = "vi";
    public static string Normalize(string? locale) =>
        locale is not null && Supported.Contains(locale.ToLowerInvariant()) ? locale.ToLowerInvariant() : Default;
}

/// <summary>Đa tiền tệ: giá lưu theo VND, quy đổi khi hiển thị theo bảng tỉ giá (cache trong bộ nhớ 5 phút).</summary>
public class CurrencyService(ShopDbContext db, Microsoft.Extensions.Caching.Memory.IMemoryCache cache)
{
    public const string Base = "VND";

    public async Task<decimal> GetRateAsync(string? currency, CancellationToken ct = default)
    {
        var code = (currency ?? Base).ToUpperInvariant();
        if (code == Base) return 1m;
        var rates = await cache.GetOrCreateAsync("fx-rates", async e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            return await db.ExchangeRates.AsNoTracking().ToDictionaryAsync(r => r.Currency, r => r.Rate, ct);
        }) ?? [];
        return rates.TryGetValue(code, out var rate)
            ? rate
            : throw new DomainException($"Tiền tệ '{code}' không được hỗ trợ.", 400, "unsupported_currency");
    }

    public static decimal Convert(decimal amountVnd, decimal rate, string currency) =>
        currency.Equals(Base, StringComparison.OrdinalIgnoreCase)
            ? Math.Round(amountVnd, 0)
            : Math.Round(amountVnd * rate, 2);
}

public record Money(decimal Amount, string Currency);

public sealed class DomainExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not DomainException ex) return;
        var problem = new ProblemDetails
        {
            Status = ex.StatusCode,
            Title = ex.Message,
            Type = $"https://myshop.local/errors/{ex.Code}",
        };
        problem.Extensions["code"] = ex.Code;
        context.Result = new ObjectResult(problem) { StatusCode = ex.StatusCode };
        context.ExceptionHandled = true;
    }
}

public static class LogMasking
{
    /// <summary>Che email khi ghi log (GDPR / Nghị định 13/2023 về bảo vệ dữ liệu cá nhân).</summary>
    public static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        return at <= 1 ? "***" : $"{email[0]}***{email[at..]}";
    }
}
