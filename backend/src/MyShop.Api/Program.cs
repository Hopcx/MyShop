using System.Security.Authentication;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using MyShop.Api.Common;
using MyShop.Api.Domain;
using MyShop.Api.Infrastructure;
using MyShop.Api.Modules;
using MyShop.Api.Modules.Auth;
using MyShop.Api.Modules.Catalog;
using MyShop.Api.Modules.Notifications;
using MyShop.Api.Modules.Orders;
using MyShop.Api.Modules.Payments;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// ── TLS: chỉ chấp nhận TLS 1.3 khi Kestrel tự terminate TLS (thường TLS được terminate ở Ingress/WAF) ──
if (config.GetValue<bool>("Security:Tls13Only"))
    builder.WebHost.ConfigureKestrel(k => k.ConfigureHttpsDefaults(h => h.SslProtocols = SslProtocols.Tls13));
builder.WebHost.ConfigureKestrel(k => k.AddServerHeader = false);

// ── Database: SQL Server (production) | SQLite (dev/test nhanh, không cần Docker) ──
builder.Services.AddDbContext<ShopDbContext>(o =>
{
    var provider = config["Database:Provider"] ?? "Sqlite";
    var cs = config.GetConnectionString("Shop") ?? "Data Source=myshop.db";
    if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
        o.UseSqlServer(cs, sql => sql.EnableRetryOnFailure(5));
    else
        o.UseSqlite(cs);
});

// ── Cache: Redis nếu cấu hình, ngược lại in-memory ──
if (config.GetConnectionString("Redis") is { Length: > 0 } redis)
    builder.Services.AddStackExchangeRedisCache(o => { o.Configuration = redis; o.InstanceName = "myshop:"; });
else
    builder.Services.AddDistributedMemoryCache();
builder.Services.AddMemoryCache();
builder.Services.AddOutputCache();

// ── Search: Elasticsearch nếu cấu hình, ngược lại SQL ──
var elastic = config.GetSection("Elasticsearch").Get<ElasticOptions>() ?? new ElasticOptions();
builder.Services.AddSingleton(elastic);
if (!string.IsNullOrEmpty(elastic.Url))
{
    builder.Services.AddHttpClient<IProductSearch, ElasticProductSearch>(c =>
    {
        c.BaseAddress = new Uri(elastic.Url.TrimEnd('/') + "/");
        c.Timeout = TimeSpan.FromSeconds(3);
    });
}
else builder.Services.AddScoped<IProductSearch, SqlProductSearch>();

// ── Modules ──
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<JwtOptions>(config.GetSection("Jwt"));
builder.Services.Configure<PricingOptions>(config.GetSection("Pricing"));
builder.Services.Configure<WebhookOptions>(config.GetSection("Webhooks"));
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<CurrencyService>();
builder.Services.AddScoped<CatalogService>();
builder.Services.AddScoped<InventoryService>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<WebhookProcessor>();

var smtp = config.GetSection("Smtp").Get<SmtpOptions>() ?? new SmtpOptions();
if (!string.IsNullOrEmpty(smtp.Host)) builder.Services.AddSingleton<IEmailSender>(new SmtpEmailSender(smtp));
else builder.Services.AddSingleton<IEmailSender, LoggingEmailSender>();
builder.Services.AddSingleton<OutboxDispatcher>();
if (config.GetValue("Outbox:Enabled", true))
    builder.Services.AddHostedService(sp => sp.GetRequiredService<OutboxDispatcher>());

// ── Auth: JWT access token ngắn hạn + refresh token xoay vòng ──
var jwt = config.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
if (System.Text.Encoding.UTF8.GetByteCount(jwt.SigningKey) < 32)
    throw new InvalidOperationException("Jwt:SigningKey phải dài ít nhất 32 byte (đặt qua biến môi trường Jwt__SigningKey).");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = jwt.Issuer, ValidAudience = jwt.Audience, IssuerSigningKey = TokenService.Key(jwt.SigningKey),
        ValidateIssuerSigningKey = true, ClockSkew = TimeSpan.FromSeconds(30),
    };
});
builder.Services.AddAuthorization();

// ── Rate limiting theo IP ──
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    string Ip(HttpContext c) => c.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    var global = config.GetValue("RateLimit:GlobalPerMinute", 600);
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(c =>
        RateLimitPartition.GetFixedWindowLimiter(Ip(c), _ => new FixedWindowRateLimiterOptions { PermitLimit = global, Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy("auth", c => RateLimitPartition.GetSlidingWindowLimiter(Ip(c), _ => new SlidingWindowRateLimiterOptions
    {
        PermitLimit = config.GetValue("RateLimit:AuthPerMinute", 10), Window = TimeSpan.FromMinutes(1), SegmentsPerWindow = 6,
    }));
    o.AddPolicy("checkout", c => RateLimitPartition.GetFixedWindowLimiter(Ip(c), _ => new FixedWindowRateLimiterOptions
    {
        PermitLimit = config.GetValue("RateLimit:CheckoutPerMinute", 20), Window = TimeSpan.FromMinutes(1),
    }));
});

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(config.GetSection("App:CorsOrigins").Get<string[]>() ?? ["http://localhost:3000"])
    .AllowAnyHeader().AllowAnyMethod()));

builder.Services.AddControllers(o => o.Filters.Add<DomainExceptionFilter>())
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi("v1", o => o.AddDocumentTransformer((doc, _, _) =>
{
    doc.Info = new() { Title = "MyShop API", Version = "v1", Description = "Nền tảng bán hàng trực tuyến — REST API v1" };
    return Task.CompletedTask;
}));
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear(); // Chạy sau Ingress trong cluster; giới hạn bằng NetworkPolicy.
    o.KnownProxies.Clear();
});

var app = builder.Build();

// ── Tiện ích CLI: xuất DDL cho DBA review / chạy migration thủ công ──
if (args.Contains("--export-schema"))
{
    using var scope = app.Services.CreateScope();
    var path = args.SkipWhile(a => a != "--export-schema").Skip(1).FirstOrDefault() ?? "schema.sql";
    await File.WriteAllTextAsync(path, scope.ServiceProvider.GetRequiredService<ShopDbContext>().Database.GenerateCreateScript());
    Console.WriteLine($"Schema exported to {path}");
    return;
}

if (config.GetValue<bool>("Database:AutoCreate"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
    await db.Database.EnsureCreatedAsync();
    if (config.GetValue<bool>("Seed:Enabled")) await Seed.RunAsync(scope.ServiceProvider);
    if (scope.ServiceProvider.GetRequiredService<IProductSearch>() is ElasticProductSearch es)
    {
        try { await es.ReindexAllAsync(CancellationToken.None); }
        catch (Exception ex) { app.Logger.LogWarning(ex, "Elasticsearch reindex failed — SQL fallback sẽ được dùng"); }
    }
}

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseMiddleware<SecurityHeadersMiddleware>();
if (!app.Environment.IsDevelopment()) app.UseHsts();
app.UseHttpMetrics();
app.UseCors();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.UseOutputCache();

app.MapOpenApi();
app.MapControllers();
app.MapHealthChecks("/health/live", new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new() { Predicate = c => c.Tags.Contains("ready") });
app.MapMetrics("/metrics");

app.Run();

public partial class Program;
