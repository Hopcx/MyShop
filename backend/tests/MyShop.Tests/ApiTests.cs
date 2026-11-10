using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyShop.Api.Infrastructure;
using MyShop.Api.Modules.Notifications;
using MyShop.Api.Modules.Payments;

namespace MyShop.Tests;

public class ShopFactory : WebApplicationFactory<Program>
{
    public const string PaySecret = "whsec_test_pay_0123456789abcdef";
    public const string ShipSecret = "whsec_test_ship_0123456789abcdef";
    public const string AdminEmail = "admin@myshop.test";
    public readonly string AdminPassword = "T3st-" + Guid.NewGuid().ToString("N")[..12];
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"myshop-test-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:Provider", "Sqlite");
        builder.UseSetting("ConnectionStrings:Shop", $"Data Source={_dbPath}");
        builder.UseSetting("Database:AutoCreate", "true");
        builder.UseSetting("Seed:Enabled", "true");
        builder.UseSetting("Seed:AdminEmail", AdminEmail);
        builder.UseSetting("Seed:AdminPassword", AdminPassword);
        builder.UseSetting("Jwt:SigningKey", "test-signing-key-" + new string('x', 40));
        builder.UseSetting("Webhooks:Secrets:mockpay", PaySecret);
        builder.UseSetting("Webhooks:Secrets:ghn", ShipSecret);
        builder.UseSetting("Webhooks:EnableMockPay", "true");
        builder.UseSetting("Outbox:Enabled", "false");
        builder.UseSetting("RateLimit:GlobalPerMinute", "100000");
        builder.UseSetting("RateLimit:AuthPerMinute", "100000");
        builder.UseSetting("RateLimit:CheckoutPerMinute", "100000");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch (IOException) { }
    }
}

public class ApiTests(ShopFactory factory) : IClassFixture<ShopFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private readonly HttpClient _client = factory.CreateClient();

    private static async Task<JsonNode> ReadAsync(HttpResponseMessage res, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.StatusCode == expected, $"Expected {expected}, got {(int)res.StatusCode}: {body}");
        return JsonNode.Parse(body)!;
    }

    private async Task<string> LoginAsync(string email, string password)
    {
        var res = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        return (await ReadAsync(res))["accessToken"]!.GetValue<string>();
    }

    private async Task<string> RegisterAsync()
    {
        var res = await _client.PostAsJsonAsync("/api/v1/auth/register",
            new { email = $"u{Guid.NewGuid():N}@example.com", password = "Password!234", fullName = "Khách Test" });
        return (await ReadAsync(res))["accessToken"]!.GetValue<string>();
    }

    private HttpRequestMessage Authed(HttpMethod method, string url, string token, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) req.Content = JsonContent.Create(body);
        return req;
    }

    /// <summary>Tìm 1 variant còn hàng ≥ qty.</summary>
    private async Task<(Guid VariantId, int Available)> InStockVariantAsync(int qty = 1)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
        var v = await db.Variants.Include(x => x.Inventory)
            .Where(x => x.Inventory!.OnHand - x.Inventory.Reserved >= qty && x.Inventory.OnHand - x.Inventory.Reserved < 1000)
            .OrderBy(x => x.Sku).FirstAsync();
        return (v.Id, v.Inventory!.Available);
    }

    private async Task<int> AvailableAsync(Guid variantId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
        return (await db.Inventory.AsNoTracking().FirstAsync(i => i.VariantId == variantId)).Available;
    }

    private async Task<Guid> CartWithAsync(Guid variantId, int qty, string? token = null)
    {
        var create = token is null ? await _client.PostAsync("/api/v1/carts", null) : await _client.SendAsync(Authed(HttpMethod.Post, "/api/v1/carts", token));
        var cartId = Guid.Parse((await ReadAsync(create, HttpStatusCode.Created))["id"]!.GetValue<string>());
        var add = token is null
            ? await _client.PostAsJsonAsync($"/api/v1/carts/{cartId}/items", new { variantId, quantity = qty })
            : await _client.SendAsync(Authed(HttpMethod.Post, $"/api/v1/carts/{cartId}/items", token, new { variantId, quantity = qty }));
        await ReadAsync(add);
        return cartId;
    }

    private static object Address => new { fullName = "Nguyễn Văn A", phone = "0901234567", line1 = "1 Lê Lợi", district = "Quận 1", city = "TP.HCM" };

    private async Task<HttpResponseMessage> PostSignedAsync(string url, string secret, object payload)
    {
        var body = JsonSerializer.Serialize(payload, Json);
        var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        req.Headers.Add(WebhookSignature.Header, WebhookSignature.Sign(secret, body, DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        return await _client.SendAsync(req);
    }

    [Fact]
    public async Task Catalog_FilterSortLocaleCurrency()
    {
        var list = await ReadAsync(await _client.GetAsync("/api/v1/products?category=thoi-trang&sort=price_asc&pageSize=50"));
        var prices = list["items"]!.AsArray().Select(i => i!["price"]!.GetValue<decimal>()).ToList();
        Assert.True(prices.Count >= 10);
        Assert.Equal(prices.OrderBy(p => p), prices);

        var search = await ReadAsync(await _client.GetAsync("/api/v1/products?q=iphone"));
        Assert.Contains(search["items"]!.AsArray(), i => i!["slug"]!.GetValue<string>() == "dien-thoai-iphone-16");

        var en = await ReadAsync(await _client.GetAsync("/api/v1/products/ao-thun-cotton-basic?locale=en&currency=USD"));
        Assert.Equal("Basic cotton T-shirt", en["name"]!.GetValue<string>());
        Assert.Equal("USD", en["currency"]!.GetValue<string>());
        Assert.True(en["variants"]!.AsArray()[0]!["price"]!.GetValue<decimal>() < 10);

        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/v1/products/khong-ton-tai")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/v1/products?sort=drop_table")).StatusCode);
    }

    [Fact]
    public async Task GuestCheckout_MockPay_FullLifecycle()
    {
        var (variantId, before) = await InStockVariantAsync(2);
        var cartId = await CartWithAsync(variantId, 2);

        var quote = await ReadAsync(await _client.PostAsJsonAsync("/api/v1/checkout/quote", new { cartId, promotionCode = "WELCOME10" }));
        Assert.Equal("WELCOME10", quote["appliedPromotion"]!.GetValue<string>());
        Assert.True(quote["discount"]!.GetValue<decimal>() > 0);

        var checkout = await ReadAsync(await _client.PostAsJsonAsync("/api/v1/checkout", new
        {
            cartId, email = "Guest@Example.com", shippingAddress = Address, paymentMethod = "MockPay", promotionCode = "WELCOME10",
        }), HttpStatusCode.Created);
        var orderId = Guid.Parse(checkout["order"]!["id"]!.GetValue<string>());
        var number = checkout["order"]!["number"]!.GetValue<string>();
        var paymentId = Guid.Parse(checkout["paymentId"]!.GetValue<string>());
        Assert.Equal("Created", checkout["order"]!["status"]!.GetValue<string>());
        Assert.Equal(before - 2, await AvailableAsync(variantId));

        // Giỏ đã checkout không dùng lại được.
        Assert.Equal(HttpStatusCode.Conflict, (await _client.PostAsJsonAsync("/api/v1/checkout", new
        {
            cartId, email = "guest@example.com", shippingAddress = Address, paymentMethod = "Cod",
        })).StatusCode);

        var payment = await ReadAsync(await _client.GetAsync($"/api/v1/payments/{paymentId}"));
        var amount = payment["amountVnd"]!.GetValue<decimal>();

        // Webhook chữ ký sai bị từ chối.
        var forged = await PostSignedAsync("/api/v1/webhooks/payments/mockpay", "wrong-secret",
            new PaymentWebhook("evt_forged", "payment.succeeded", paymentId, "x", amount));
        Assert.Equal(HttpStatusCode.Unauthorized, forged.StatusCode);

        var evt = new PaymentWebhook($"evt_{Guid.NewGuid():N}", "payment.succeeded", paymentId, "pg_123", amount);
        var first = await ReadAsync(await PostSignedAsync("/api/v1/webhooks/payments/mockpay", ShopFactory.PaySecret, evt));
        Assert.False(first["duplicate"]!.GetValue<bool>());
        var replay = await ReadAsync(await PostSignedAsync("/api/v1/webhooks/payments/mockpay", ShopFactory.PaySecret, evt));
        Assert.True(replay["duplicate"]!.GetValue<bool>());

        var lookup = await ReadAsync(await _client.GetAsync($"/api/v1/orders/lookup?number={number}&email=guest@example.com"));
        Assert.Equal("Confirmed", lookup["status"]!.GetValue<string>());
        Assert.Equal("Succeeded", lookup["paymentStatus"]!.GetValue<string>());

        // Admin giao hàng → webhook vận chuyển "delivered" → Completed.
        var admin = await LoginAsync(ShopFactory.AdminEmail, factory.AdminPassword);
        var shipped = await ReadAsync(await _client.SendAsync(Authed(HttpMethod.Post, $"/api/v1/admin/orders/{orderId}/ship", admin, new { carrier = "GHN" })));
        Assert.Equal("Shipping", shipped["status"]!.GetValue<string>());
        var tracking = shipped["trackingNumber"]!.GetValue<string>();

        await ReadAsync(await PostSignedAsync("/api/v1/webhooks/shipping/ghn", ShopFactory.ShipSecret,
            new ShippingWebhook($"ship_{Guid.NewGuid():N}", tracking, "delivered")));
        var done = await ReadAsync(await _client.GetAsync($"/api/v1/orders/lookup?number={number}&email=guest@example.com"));
        Assert.Equal("Completed", done["status"]!.GetValue<string>());
        Assert.Equal(3, done["history"]!.AsArray().Count);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
            var inv = await db.Inventory.AsNoTracking().FirstAsync(i => i.VariantId == variantId);
            Assert.Equal(before - 2, inv.Available);
            Assert.True(await db.InventoryMovements.AnyAsync(m => m.VariantId == variantId && m.Type == MyShop.Api.Domain.InventoryMovementType.Ship));

            // Outbox → notifications
            await scope.ServiceProvider.GetRequiredService<OutboxDispatcher>().ProcessBatchAsync(CancellationToken.None);
            Assert.True(await db.Notifications.AnyAsync(n => n.Recipient == "guest@example.com"));
        }

        var report = await ReadAsync(await _client.SendAsync(Authed(HttpMethod.Get, "/api/v1/admin/reports/summary", admin)));
        Assert.True(report["orders"]!.GetValue<int>() >= 1);
    }

    [Fact]
    public async Task PaymentFailure_CancelsOrder_AndReleasesStock()
    {
        var (variantId, before) = await InStockVariantAsync();
        var cartId = await CartWithAsync(variantId, 1);
        var checkout = await ReadAsync(await _client.PostAsJsonAsync("/api/v1/checkout",
            new { cartId, email = "fail@example.com", shippingAddress = Address, paymentMethod = "MockPay" }), HttpStatusCode.Created);
        var paymentId = checkout["paymentId"]!.GetValue<string>();
        Assert.Equal(before - 1, await AvailableAsync(variantId));

        await ReadAsync(await _client.PostAsync($"/api/v1/payments/mockpay/{paymentId}/simulate?success=false", null));
        Assert.Equal(before, await AvailableAsync(variantId));
    }

    [Fact]
    public async Task AddToCart_BeyondStock_Returns409()
    {
        var (variantId, available) = await InStockVariantAsync();
        var res = await _client.PostAsync("/api/v1/carts", null);
        var cartId = (await ReadAsync(res, HttpStatusCode.Created))["id"]!.GetValue<string>();
        var add = await _client.PostAsJsonAsync($"/api/v1/carts/{cartId}/items", new { variantId, quantity = Math.Min(99, available + 1) });
        if (available + 1 <= 99) Assert.Equal(HttpStatusCode.Conflict, add.StatusCode);
    }

    [Fact]
    public async Task Auth_RefreshRotation_ReuseDetection_AndRoles()
    {
        var email = $"r{Guid.NewGuid():N}@example.com";
        var reg = await ReadAsync(await _client.PostAsJsonAsync("/api/v1/auth/register", new { email, password = "Password!234", fullName = "R" }));
        var refresh1 = reg["refreshToken"]!.GetValue<string>();

        var rotated = await ReadAsync(await _client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = refresh1 }));
        var refresh2 = rotated["refreshToken"]!.GetValue<string>();
        Assert.NotEqual(refresh1, refresh2);

        // Dùng lại token cũ → 401 và cả family bị thu hồi.
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = refresh1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = refresh2 })).StatusCode);

        Assert.Equal(HttpStatusCode.Conflict, (await _client.PostAsJsonAsync("/api/v1/auth/register", new { email, password = "Password!234", fullName = "R" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "wrong-password" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/v1/auth/register", new { email = "not-an-email", password = "x", fullName = "" })).StatusCode);

        var customer = await LoginAsync(email, "Password!234");
        Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(Authed(HttpMethod.Get, "/api/v1/me", customer))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendAsync(Authed(HttpMethod.Get, "/api/v1/admin/reports/summary", customer))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/v1/admin/orders")).StatusCode);
    }

    [Fact]
    public async Task LoggedIn_Checkout_Cod_OrderHistory_AndCancel()
    {
        var token = await RegisterAsync();
        var (variantId, before) = await InStockVariantAsync();
        var cartId = await CartWithAsync(variantId, 1, token);
        var res = await _client.SendAsync(Authed(HttpMethod.Post, "/api/v1/checkout", token,
            new { cartId, email = "member@example.com", shippingAddress = Address, paymentMethod = "Cod" }));
        var orderId = (await ReadAsync(res, HttpStatusCode.Created))["order"]!["id"]!.GetValue<string>();

        var mine = await ReadAsync(await _client.SendAsync(Authed(HttpMethod.Get, "/api/v1/orders", token)));
        Assert.Equal(1, mine["total"]!.GetValue<int>());

        var other = await RegisterAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await _client.SendAsync(Authed(HttpMethod.Get, $"/api/v1/orders/{orderId}", other))).StatusCode);

        var cancelled = await ReadAsync(await _client.SendAsync(Authed(HttpMethod.Post, $"/api/v1/orders/{orderId}/cancel", token)));
        Assert.Equal("Cancelled", cancelled["status"]!.GetValue<string>());
        Assert.Equal(before, await AvailableAsync(variantId));
    }

    [Fact]
    public async Task Admin_CsvImport_CreatesProducts()
    {
        var admin = await LoginAsync(ShopFactory.AdminEmail, factory.AdminPassword);
        var csv = "slug,name,brand,category,description,sku,variant_name,color,size,price,compare_at_price,stock,image_url\n" +
                  "mu-luoi-trai,\"Mũ lưỡi trai, cotton\",MyShop,phu-kien,Mũ đẹp,MU-01,Đen,Đen,,150000,,10,https://picsum.photos/seed/mu/800/800\n" +
                  "mu-luoi-trai,\"Mũ lưỡi trai, cotton\",MyShop,phu-kien,Mũ đẹp,MU-02,Trắng,Trắng,,140000,,5,\n" +
                  "sai-danh-muc,Sản phẩm lỗi,X,khong-co,,ERR-01,,,,1000,,1,\n";
        using var form = new MultipartFormDataContent { { new StringContent(csv), "file", "catalog.csv" } };
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/products/import") { Content = form };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", admin);
        var report = await ReadAsync(await _client.SendAsync(req));
        Assert.Equal(1, report["created"]!.GetValue<int>());
        Assert.Single(report["errors"]!.AsArray());

        var product = await ReadAsync(await _client.GetAsync("/api/v1/products/mu-luoi-trai"));
        Assert.Equal("Mũ lưỡi trai, cotton", product["name"]!.GetValue<string>());
        Assert.Equal(2, product["variants"]!.AsArray().Count);
        Assert.Equal(140000, product["variants"]!.AsArray()[0]!["price"]!.GetValue<decimal>());
    }

    [Fact]
    public async Task SecurityHeaders_Health_OpenApi()
    {
        var res = await _client.GetAsync("/api/v1/categories");
        Assert.Equal("nosniff", res.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", res.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("default-src 'none'", res.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/health/ready")).StatusCode);
        var openapi = await ReadAsync(await _client.GetAsync("/openapi/v1.json"));
        Assert.NotNull(openapi["paths"]!["/api/v1/checkout"]);
    }
}
