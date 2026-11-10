using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using MyShop.Api.Common;
using MyShop.Api.Domain;
using MyShop.Api.Infrastructure;

namespace MyShop.Api.Modules.Catalog;

[ApiController]
[Route("api/v1")]
public class CatalogController(CatalogService catalog) : ControllerBase
{
    [HttpGet("products")]
    public Task<PagedResult<ProductSummaryDto>> List([FromQuery] ProductQuery q, CancellationToken ct) => catalog.ListAsync(q, ct);

    [HttpGet("products/{slug}")]
    public Task<ProductDetailDto> Get(string slug, [FromQuery] string? locale, [FromQuery] string? currency, CancellationToken ct) =>
        catalog.GetAsync(slug, locale, currency, ct);

    [HttpGet("categories")]
    [OutputCache(Duration = 300)]
    public Task<List<CategoryDto>> Categories(CancellationToken ct) => catalog.CategoriesAsync(ct);

    [HttpGet("brands")]
    [OutputCache(Duration = 300)]
    public Task<List<string>> Brands(CancellationToken ct) => catalog.BrandsAsync(ct);

    [HttpGet("currencies")]
    public async Task<IActionResult> Currencies([FromServices] ShopDbContext db, CancellationToken ct)
    {
        var list = await db.ExchangeRates.AsNoTracking().Select(r => new { r.Currency, r.Rate }).ToListAsync(ct);
        list.Insert(0, new { Currency = CurrencyService.Base, Rate = 1m });
        return Ok(list);
    }
}

// ───────────── Reviews ─────────────
public record ReviewRequest([Range(1, 5)] int Rating, [Required, MaxLength(2000)] string Comment);
public record ReviewDto(Guid Id, string Author, int Rating, string Comment, bool VerifiedPurchase, DateTime CreatedAt);

[ApiController]
[Route("api/v1/products/{productId:guid}/reviews")]
public class ReviewsController(ShopDbContext db, CatalogService catalog) : ControllerBase
{
    [HttpGet]
    public async Task<PagedResult<ReviewDto>> List(Guid productId, [FromQuery, Range(1, 1000)] int page = 1, CancellationToken ct = default)
    {
        const int size = 10;
        var q = db.Reviews.AsNoTracking().Where(r => r.ProductId == productId);
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(r => r.CreatedAt).Skip((page - 1) * size).Take(size)
            .Select(r => new ReviewDto(r.Id, r.User!.FullName, r.Rating, r.Comment, r.VerifiedPurchase, r.CreatedAt))
            .ToListAsync(ct);
        return new PagedResult<ReviewDto>(items, page, size, total);
    }

    [HttpPost]
    [Authorize]
    public async Task<ActionResult<ReviewDto>> Create(Guid productId, ReviewRequest req, CancellationToken ct)
    {
        var uid = User.RequiredUserId();
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == productId, ct) ?? throw DomainException.NotFound("Sản phẩm");
        if (await db.Reviews.AnyAsync(r => r.ProductId == productId && r.UserId == uid, ct))
            throw DomainException.Conflict("Bạn đã đánh giá sản phẩm này.", "already_reviewed");

        var verified = await db.Orders.AnyAsync(o => o.UserId == uid && o.Status == OrderStatus.Completed
                                                     && o.Items.Any(i => i.ProductId == productId), ct);
        // Sanitization: loại bỏ thẻ HTML, lưu plain text (frontend React luôn escape khi render) → chống stored-XSS.
        var review = new Review
        {
            ProductId = productId, UserId = uid, Rating = req.Rating,
            Comment = System.Text.RegularExpressions.Regex.Replace(req.Comment, "<[^>]*>", "").Trim(), VerifiedPurchase = verified,
        };
        db.Reviews.Add(review);
        product.RatingAverage = (product.RatingAverage * product.RatingCount + req.Rating) / (product.RatingCount + 1);
        product.RatingCount++;
        await db.SaveChangesAsync(ct);
        await catalog.InvalidateAsync(ct);
        var author = (await db.Users.FindAsync([uid], ct))!.FullName;
        return new ReviewDto(review.Id, author, review.Rating, review.Comment, verified, review.CreatedAt);
    }
}

// ───────────── Admin catalog ─────────────
public record VariantRequest([Required, MaxLength(64)] string Sku, [Required, MaxLength(200)] string Name, string? Color, string? Size,
    [Range(0, 1_000_000_000)] decimal Price, decimal? CompareAtPrice, [Range(0, 1_000_000)] int Stock);

public record ProductRequest(
    [Required, RegularExpression("^[a-z0-9]+(?:-[a-z0-9]+)*$"), MaxLength(160)] string Slug,
    [Required, MaxLength(300)] string Name,
    [MaxLength(8000)] string? Description,
    [MaxLength(120)] string? Brand,
    [Required] string CategorySlug,
    [MinLength(1)] List<VariantRequest> Variants,
    List<string>? Images,
    string? NameEn,
    string? DescriptionEn,
    bool IsActive = true);

public record ImportReport(int Created, int Updated, int VariantsUpserted, List<string> Errors);

[ApiController]
[Authorize(Roles = Roles.Admin)]
[Route("api/v1/admin/products")]
public class AdminProductsController(ShopDbContext db, CatalogService catalog, InventoryService inventory) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(ProductRequest req, CancellationToken ct)
    {
        if (await db.Products.AnyAsync(p => p.Slug == req.Slug, ct)) throw DomainException.Conflict("Slug đã tồn tại.");
        var product = await UpsertAsync(null, req, ct);
        return Created($"/api/v1/products/{product.Slug}", new { product.Id, product.Slug });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, ProductRequest req, CancellationToken ct)
    {
        var product = await db.Products.Include(p => p.Variants).Include(p => p.Images).Include(p => p.Translations)
            .FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw DomainException.NotFound("Sản phẩm");
        await UpsertAsync(product, req, ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct)
    {
        // Soft delete: giữ lịch sử đơn hàng tham chiếu tới sản phẩm.
        var product = await db.Products.FindAsync([id], ct) ?? throw DomainException.NotFound("Sản phẩm");
        product.IsActive = false;
        product.UpdatedAt = DateTime.UtcNow;
        db.Enqueue("ProductChanged", new { ProductId = id });
        await db.SaveChangesAsync(ct);
        await catalog.InvalidateAsync(ct);
        return NoContent();
    }

    private async Task<Product> UpsertAsync(Product? product, ProductRequest req, CancellationToken ct)
    {
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Slug == req.CategorySlug, ct)
                       ?? throw new DomainException($"Danh mục '{req.CategorySlug}' không tồn tại.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (product is null)
        {
            product = new Product { Slug = req.Slug, Name = req.Name };
            db.Products.Add(product);
        }
        product.Slug = req.Slug;
        product.Name = req.Name;
        product.Description = req.Description ?? "";
        product.Brand = req.Brand ?? "";
        product.CategoryId = category.Id;
        product.IsActive = req.IsActive;
        product.UpdatedAt = DateTime.UtcNow;

        var stockChanges = new List<(ProductVariant Variant, int Stock)>();
        foreach (var v in req.Variants)
        {
            var variant = product.Variants.FirstOrDefault(x => x.Sku == v.Sku);
            if (variant is null)
            {
                variant = new ProductVariant { Sku = v.Sku, Name = v.Name, ProductId = product.Id };
                product.Variants.Add(variant);
            }
            variant.Name = v.Name; variant.Color = v.Color; variant.Size = v.Size;
            variant.Price = v.Price; variant.CompareAtPrice = v.CompareAtPrice;
            stockChanges.Add((variant, v.Stock));
        }
        CatalogService.RecomputeMinPrice(product);

        if (req.Images is { Count: > 0 })
        {
            db.ProductImages.RemoveRange(product.Images);
            product.Images = req.Images.Select((url, i) => new ProductImage { Url = url, Alt = req.Name, SortOrder = i }).ToList();
        }
        if (req.NameEn is not null)
        {
            var en = product.Translations.FirstOrDefault(t => t.Locale == "en");
            if (en is null) product.Translations.Add(new ProductTranslation { Locale = "en", Name = req.NameEn, Description = req.DescriptionEn ?? "" });
            else { en.Name = req.NameEn; en.Description = req.DescriptionEn ?? ""; }
        }
        await db.SaveChangesAsync(ct);

        foreach (var (variant, stock) in stockChanges)
            await inventory.SetOnHandAsync(variant.Id, stock, $"admin:{product.Slug}", ct);

        db.Enqueue("ProductChanged", new { ProductId = product.Id });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await catalog.InvalidateAsync(ct);
        return product;
    }

    /// <summary>
    /// ETL import catalog từ CSV (UTF-8, có header). Mỗi dòng = 1 variant, gom theo slug.
    /// Cột: slug,name,brand,category,description,sku,variant_name,color,size,price,compare_at_price,stock,image_url
    /// </summary>
    [HttpPost("import")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<ImportReport> Import(IFormFile file, CancellationToken ct)
    {
        var errors = new List<string>();
        using var reader = new StreamReader(file.OpenReadStream());
        var header = Csv.ParseLine(await reader.ReadLineAsync(ct) ?? "").Select(h => h.Trim().ToLowerInvariant()).ToList();
        string[] required = ["slug", "name", "category", "sku", "price"];
        var missing = required.Where(r => !header.Contains(r)).ToList();
        if (missing.Count > 0) throw new DomainException($"Thiếu cột: {string.Join(", ", missing)}");

        var rows = new List<Dictionary<string, string>>();
        var lineNo = 1;
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            lineNo++;
            if (string.IsNullOrWhiteSpace(line)) continue;
            var cells = Csv.ParseLine(line);
            if (cells.Count != header.Count) { errors.Add($"Dòng {lineNo}: số cột không khớp header."); continue; }
            var row = header.Zip(cells).ToDictionary(x => x.First, x => x.Second.Trim());
            row["__line"] = lineNo.ToString();
            rows.Add(row);
        }

        int created = 0, updated = 0, variants = 0;
        foreach (var group in rows.GroupBy(r => r["slug"]))
        {
            try
            {
                var first = group.First();
                var vreqs = group.Select(r => new VariantRequest(r["sku"], Get(r, "variant_name") ?? r["name"], Get(r, "color"), Get(r, "size"),
                    decimal.Parse(r["price"], CultureInfo.InvariantCulture),
                    Get(r, "compare_at_price") is { } cap ? decimal.Parse(cap, CultureInfo.InvariantCulture) : null,
                    Get(r, "stock") is { } s ? int.Parse(s, CultureInfo.InvariantCulture) : 0)).ToList();
                var req = new ProductRequest(group.Key, first["name"], Get(first, "description"), Get(first, "brand"), first["category"], vreqs,
                    group.Select(r => Get(r, "image_url")).OfType<string>().Distinct().ToList(), null, null);

                var validation = new List<ValidationResult>();
                if (!Validator.TryValidateObject(req, new ValidationContext(req), validation, true))
                    throw new DomainException(string.Join("; ", validation.Select(v => v.ErrorMessage)));

                var existing = await db.Products.Include(p => p.Variants).Include(p => p.Images).Include(p => p.Translations)
                    .FirstOrDefaultAsync(p => p.Slug == group.Key, ct);
                await UpsertAsync(existing, req, ct);
                if (existing is null) created++; else updated++;
                variants += vreqs.Count;
            }
            catch (Exception ex) when (ex is DomainException or FormatException or DbUpdateException)
            {
                errors.Add($"Sản phẩm '{group.Key}' (dòng {group.First()["__line"]}): {ex.Message}");
                db.ChangeTracker.Clear();
            }
        }
        return new ImportReport(created, updated, variants, errors);

        static string? Get(Dictionary<string, string> r, string k) => r.TryGetValue(k, out var v) && v.Length > 0 ? v : null;
    }
}

public static class Csv
{
    /// <summary>Parser CSV tối giản theo RFC 4180 (hỗ trợ dấu nháy kép và "" escape trong 1 dòng).</summary>
    public static List<string> ParseLine(string line)
    {
        var result = new List<string>();
        var sb = new System.Text.StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else if (c == '"') inQuotes = false;
                else sb.Append(c);
            }
            else if (c == '"') inQuotes = true;
            else if (c == ',') { result.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        result.Add(sb.ToString());
        return result;
    }
}
