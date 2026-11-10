using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MyShop.Api.Infrastructure;

/// <summary>Security headers kiểu "helmet" cho API: CSP chặt, chống MIME sniffing, clickjacking, HSTS.</summary>
public class SecurityHeadersMiddleware(RequestDelegate next, IWebHostEnvironment env)
{
    public Task InvokeAsync(HttpContext ctx)
    {
        ctx.Response.OnStarting(() =>
        {
            var h = ctx.Response.Headers;
            // API chỉ trả JSON → CSP khóa toàn bộ; trang OpenAPI dev được nới riêng.
            h.ContentSecurityPolicy = ctx.Request.Path.StartsWithSegments("/openapi")
                ? "default-src 'self'; style-src 'self' 'unsafe-inline'"
                : "default-src 'none'; frame-ancestors 'none'; base-uri 'none'";
            h.XContentTypeOptions = "nosniff";
            h.XFrameOptions = "DENY";
            h["Referrer-Policy"] = "no-referrer";
            h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            h["Cross-Origin-Opener-Policy"] = "same-origin";
            h["Cross-Origin-Resource-Policy"] = "same-site";
            if (!env.IsDevelopment()) h.StrictTransportSecurity = "max-age=63072000; includeSubDomains; preload";
            h.Remove("Server");
            h.Remove("X-Powered-By");
            return Task.CompletedTask;
        });
        return next(ctx);
    }
}

public class DatabaseHealthCheck(ShopDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default) =>
        await db.Database.CanConnectAsync(ct) ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("Database unreachable");
}
