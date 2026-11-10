using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using MyShop.Api.Common;
using MyShop.Api.Domain;
using MyShop.Api.Infrastructure;

namespace MyShop.Api.Modules.Catalog;

public record ProductQuery
{
    [MaxLength(100)] public string? Q { get; init; }
    [MaxLength(120)] public string? Category { get; init; }
    [MaxLength(120)] public string? Brand { get; init; }
    [Range(0, double.MaxValue)] public decimal? MinPrice { get; init; }
    [Range(0, double.MaxValue)] public decimal? MaxPrice { get; init; }
    [RegularExpression("^(newest|price_asc|price_desc|rating|name)$")] public string Sort { get; init; } = "newest";
    [Range(1, 1000)] public int Page { get; init; } = 1;
    [Range(1, 60)] public int PageSize { get; init; } = 24;
    [RegularExpression("^(vi|en)$")] public string? Locale { get; init; }
    [RegularExpression("^[A-Za-z]{3}$")] public string? Currency { get; init; }
}

public record ProductSummaryDto(Guid Id, string Slug, string Name, string Brand, string CategorySlug,
    decimal Price, decimal? CompareAtPrice, string Currency, string? Image, double Rating, int RatingCount, bool InStock);

public record VariantDto(Guid Id, string Sku, string Name, string? Color, string? Size, decimal Price,
    decimal? CompareAtPrice, int Available);

public record ImageDto(string Url, string Alt);

public record ProductDetailDto(Guid Id, string Slug, string Name, string Description, string Brand,
    string CategorySlug, string CategoryName, string Currency, double Rating, int RatingCount,
    IReadOnlyList<VariantDto> Variants, IReadOnlyList<ImageDto> Images, IReadOnlyList<string> Colors,
    IReadOnlyList<string> Sizes);

public record CategoryDto(Guid Id, string Slug, string Name, Guid? ParentId);

/// <summary>Kết quả tìm kiếm thuần id để có thể hoán đổi backend (SQL / Elasticsearch).</summary>
public record SearchHits(IReadOnlyList<Guid> Ids, int Total);

public interface IProductSearch
{
    Task<SearchHits> SearchAsync(ProductQuery q, CancellationToken ct);
    Task IndexAsync(Guid productId, CancellationToken ct);
    Task RemoveAsync(Guid productId, CancellationToken ct);
}

/// <summary>Tìm kiếm trực tiếp trên SQL — mặc định cho dev và fallback khi Elasticsearch không khả dụng.</summary>
public class SqlProductSearch(ShopDbContext db) : IProductSearch
{
    public async Task<SearchHits> SearchAsync(ProductQuery q, CancellationToken ct)
    {
        var query = db.Products.AsNoTracking().Where(p => p.IsActive);
        if (!string.IsNullOrWhiteSpace(q.Q))
        {
            var pattern = $"%{EscapeLike(q.Q.Trim())}%";
            query = query.Where(p => EF.Functions.Like(p.Name, pattern, "\\") || EF.Functions.Like(p.Brand, pattern, "\\")
                                     || p.Translations.Any(t => EF.Functions.Like(t.Name, pattern, "\\")));
        }
        if (!string.IsNullOrWhiteSpace(q.Category))
        {
            var cat = await db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Slug == q.Category, ct);
            if (cat is null) return new SearchHits([], 0);
            var childIds = await db.Categories.Where(c => c.ParentId == cat.Id).Select(c => c.Id).ToListAsync(ct);
            childIds.Add(cat.Id);
            query = query.Where(p => childIds.Contains(p.CategoryId));
        }
        if (!string.IsNullOrWhiteSpace(q.Brand)) query = query.Where(p => p.Brand == q.Brand);
        if (q.MinPrice is { } min) query = query.Where(p => p.MinPrice >= min);
        if (q.MaxPrice is { } max) query = query.Where(p => p.MinPrice <= max);

        query = q.Sort switch
        {
            "price_asc" => query.OrderBy(p => p.MinPrice).ThenBy(p => p.Id),
            "price_desc" => query.OrderByDescending(p => p.MinPrice).ThenBy(p => p.Id),
            "rating" => query.OrderByDescending(p => p.RatingAverage).ThenByDescending(p => p.RatingCount).ThenBy(p => p.Id),
            "name" => query.OrderBy(p => p.Name).ThenBy(p => p.Id),
            _ => query.OrderByDescending(p => p.CreatedAt).ThenBy(p => p.Id),
        };

        var total = await query.CountAsync(ct);
        var ids = await query.Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).Select(p => p.Id).ToListAsync(ct);
        return new SearchHits(ids, total);
    }

    public Task IndexAsync(Guid productId, CancellationToken ct) => Task.CompletedTask;
    public Task RemoveAsync(Guid productId, CancellationToken ct) => Task.CompletedTask;

    private static string EscapeLike(string s) => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}

public class CatalogService(ShopDbContext db, IProductSearch search, IDistributedCache cache, CurrencyService fx,
    ILogger<CatalogService> log)
{
    private const string VersionKey = "catalog:version";
    private static readonly DistributedCacheEntryOptions ListTtl = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60) };
    private static readonly DistributedCacheEntryOptions DetailTtl = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) };

    public async Task<PagedResult<ProductSummaryDto>> ListAsync(ProductQuery q, CancellationToken ct)
    {
        var locale = Locales.Normalize(q.Locale);
        var currency = (q.Currency ?? CurrencyService.Base).ToUpperInvariant();
        var rate = await fx.GetRateAsync(currency, ct);

        // Cache theo phiên bản catalog: mọi thay đổi sản phẩm sẽ "bump" version → cache cũ tự vô hiệu.
        var version = await GetVersionAsync(ct);
        var key = $"catalog:{version}:list:{JsonSerializer.Serialize(q with { Locale = locale, Currency = currency })}";
        var cached = await TryGetAsync<PagedResult<ProductSummaryDto>>(key, ct);
        if (cached is not null) return cached;

        SearchHits hits;
        try { hits = await search.SearchAsync(q, ct); }
        catch (Exception ex) when (search is not SqlProductSearch)
        {
            log.LogWarning(ex, "Search backend failed, falling back to SQL");
            hits = await new SqlProductSearch(db).SearchAsync(q, ct);
        }

        var rows = await db.Products.AsNoTracking()
            .Where(p => hits.Ids.Contains(p.Id))
            .Select(p => new
            {
                p.Id, p.Slug, p.Brand, p.MinPrice, p.RatingAverage, p.RatingCount,
                Name = p.Translations.Where(t => t.Locale == locale).Select(t => t.Name).FirstOrDefault() ?? p.Name,
                CategorySlug = p.Category!.Slug,
                CompareAt = p.Variants.Where(v => v.Price == p.MinPrice).Select(v => v.CompareAtPrice).FirstOrDefault(),
                Image = p.Images.OrderBy(i => i.SortOrder).Select(i => i.Url).FirstOrDefault(),
                InStock = p.Variants.Any(v => v.Inventory != null && v.Inventory.OnHand - v.Inventory.Reserved > 0),
            })
            .ToListAsync(ct);

        var byId = rows.ToDictionary(r => r.Id);
        var items = hits.Ids.Where(byId.ContainsKey).Select(id => byId[id]).Select(r => new ProductSummaryDto(
            r.Id, r.Slug, r.Name, r.Brand, r.CategorySlug,
            CurrencyService.Convert(r.MinPrice, rate, currency),
            r.CompareAt is { } c ? CurrencyService.Convert(c, rate, currency) : null,
            currency, r.Image, Math.Round(r.RatingAverage, 1), r.RatingCount, r.InStock)).ToList();

        var result = new PagedResult<ProductSummaryDto>(items, q.Page, q.PageSize, hits.Total);
        await TrySetAsync(key, result, ListTtl, ct);
        return result;
    }

    public async Task<ProductDetailDto> GetAsync(string slug, string? localeIn, string? currencyIn, CancellationToken ct)
    {
        var locale = Locales.Normalize(localeIn);
        var currency = (currencyIn ?? CurrencyService.Base).ToUpperInvariant();
        var rate = await fx.GetRateAsync(currency, ct);
        var version = await GetVersionAsync(ct);
        var key = $"catalog:{version}:detail:{slug}:{locale}:{currency}";
        var cached = await TryGetAsync<ProductDetailDto>(key, ct);
        if (cached is not null) return cached;

        var p = await db.Products.AsNoTracking().AsSplitQuery()
                    .Include(x => x.Category).Include(x => x.Images).Include(x => x.Translations)
                    .Include(x => x.Variants).ThenInclude(v => v.Inventory)
                    .FirstOrDefaultAsync(x => x.Slug == slug && x.IsActive, ct)
                ?? throw DomainException.NotFound("Sản phẩm");

        var tr = p.Translations.FirstOrDefault(t => t.Locale == locale);
        var variants = p.Variants.OrderBy(v => v.Price).ThenBy(v => v.Sku).Select(v => new VariantDto(v.Id, v.Sku, v.Name, v.Color, v.Size,
            CurrencyService.Convert(v.Price, rate, currency),
            v.CompareAtPrice is { } c ? CurrencyService.Convert(c, rate, currency) : null,
            Math.Max(0, v.Inventory?.Available ?? 0))).ToList();

        var dto = new ProductDetailDto(p.Id, p.Slug, tr?.Name ?? p.Name, tr?.Description ?? p.Description, p.Brand,
            p.Category!.Slug, p.Category.Name, currency, Math.Round(p.RatingAverage, 1), p.RatingCount, variants,
            p.Images.OrderBy(i => i.SortOrder).Select(i => new ImageDto(i.Url, i.Alt)).ToList(),
            p.Variants.Select(v => v.Color).OfType<string>().Distinct().ToList(),
            p.Variants.Select(v => v.Size).OfType<string>().Distinct().ToList());

        // Tồn kho thay đổi liên tục → detail cache ngắn hơn và client nên kiểm tra lại khi thêm vào giỏ.
        await TrySetAsync(key, dto, DetailTtl, ct);
        return dto;
    }

    public async Task<List<CategoryDto>> CategoriesAsync(CancellationToken ct) =>
        await db.Categories.AsNoTracking().OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new CategoryDto(c.Id, c.Slug, c.Name, c.ParentId)).ToListAsync(ct);

    public async Task<List<string>> BrandsAsync(CancellationToken ct) =>
        await db.Products.AsNoTracking().Where(p => p.IsActive && p.Brand != "").Select(p => p.Brand).Distinct().OrderBy(b => b).ToListAsync(ct);

    private async Task<string> GetVersionAsync(CancellationToken ct)
    {
        try { return await cache.GetStringAsync(VersionKey, ct) ?? "0"; }
        catch (Exception ex) { log.LogWarning(ex, "Cache read failed"); return "nocache-" + Guid.NewGuid(); }
    }

    public Task InvalidateAsync(CancellationToken ct) =>
        SafeAsync(() => cache.SetStringAsync(VersionKey, Guid.NewGuid().ToString("N"), ct));

    // Cache là tối ưu, không phải phụ thuộc cứng: Redis lỗi thì vẫn phục vụ từ DB.
    private async Task<T?> TryGetAsync<T>(string key, CancellationToken ct) where T : class
    {
        try
        {
            var json = await cache.GetStringAsync(key, ct);
            return json is null ? null : JsonSerializer.Deserialize<T>(json);
        }
        catch (Exception ex) { log.LogWarning(ex, "Cache read failed"); return null; }
    }

    private Task TrySetAsync<T>(string key, T value, DistributedCacheEntryOptions opt, CancellationToken ct) =>
        SafeAsync(() => cache.SetStringAsync(key, JsonSerializer.Serialize(value), opt, ct));

    private async Task SafeAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { log.LogWarning(ex, "Cache write failed"); }
    }

    /// <summary>Tính lại giá thấp nhất (denormalized) sau khi thay đổi variant.</summary>
    public static void RecomputeMinPrice(Product p) =>
        p.MinPrice = p.Variants.Count == 0 ? 0 : p.Variants.Min(v => v.Price);
}
