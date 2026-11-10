using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using MyShop.Api.Infrastructure;

namespace MyShop.Api.Modules.Catalog;

public class ElasticOptions
{
    public string? Url { get; set; }
    public string Index { get; set; } = "products";
}

/// <summary>
/// Elasticsearch qua REST API thuần (không phụ thuộc SDK). Index được cập nhật bất đồng bộ từ outbox event
/// "ProductChanged" → đảm bảo eventual consistency với SQL Server (nguồn sự thật).
/// </summary>
public class ElasticProductSearch(HttpClient http, ElasticOptions opt, ShopDbContext db) : IProductSearch
{
    public async Task<SearchHits> SearchAsync(ProductQuery q, CancellationToken ct)
    {
        var filters = new JsonArray { new JsonObject { ["term"] = new JsonObject { ["isActive"] = true } } };
        if (!string.IsNullOrWhiteSpace(q.Category))
            filters.Add(new JsonObject { ["terms"] = new JsonObject { ["categorySlugs"] = new JsonArray(q.Category) } });
        if (!string.IsNullOrWhiteSpace(q.Brand))
            filters.Add(new JsonObject { ["term"] = new JsonObject { ["brand"] = q.Brand } });
        if (q.MinPrice is not null || q.MaxPrice is not null)
        {
            var range = new JsonObject();
            if (q.MinPrice is { } min) range["gte"] = min;
            if (q.MaxPrice is { } max) range["lte"] = max;
            filters.Add(new JsonObject { ["range"] = new JsonObject { ["minPrice"] = range } });
        }

        JsonNode must = string.IsNullOrWhiteSpace(q.Q)
            ? new JsonObject { ["match_all"] = new JsonObject() }
            : new JsonObject
            {
                ["multi_match"] = new JsonObject
                {
                    ["query"] = q.Q, ["fields"] = new JsonArray("name^3", "nameEn^2", "brand^2", "description"),
                    ["fuzziness"] = "AUTO",
                },
            };

        JsonArray sort = q.Sort switch
        {
            "price_asc" => [new JsonObject { ["minPrice"] = "asc" }],
            "price_desc" => [new JsonObject { ["minPrice"] = "desc" }],
            "rating" => [new JsonObject { ["rating"] = "desc" }],
            "name" => [new JsonObject { ["name.keyword"] = "asc" }],
            _ => string.IsNullOrWhiteSpace(q.Q) ? [new JsonObject { ["createdAt"] = "desc" }] : ["_score"],
        };

        var body = new JsonObject
        {
            ["from"] = (q.Page - 1) * q.PageSize, ["size"] = q.PageSize, ["_source"] = false, ["track_total_hits"] = true,
            ["query"] = new JsonObject { ["bool"] = new JsonObject { ["must"] = must, ["filter"] = filters } },
            ["sort"] = sort,
        };

        using var res = await http.PostAsJsonAsync($"{opt.Index}/_search", body, ct);
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadFromJsonAsync<JsonNode>(ct);
        var hits = json!["hits"]!;
        var ids = hits["hits"]!.AsArray().Select(h => Guid.Parse(h!["_id"]!.GetValue<string>())).ToList();
        return new SearchHits(ids, hits["total"]!["value"]!.GetValue<int>());
    }

    public async Task IndexAsync(Guid productId, CancellationToken ct)
    {
        var p = await db.Products.AsNoTracking().Include(x => x.Category).Include(x => x.Translations)
            .FirstOrDefaultAsync(x => x.Id == productId, ct);
        if (p is null) { await RemoveAsync(productId, ct); return; }

        var slugs = new List<string> { p.Category!.Slug };
        if (p.Category.ParentId is { } parentId)
            slugs.AddRange(await db.Categories.Where(c => c.Id == parentId).Select(c => c.Slug).ToListAsync(ct));

        var doc = new
        {
            name = p.Name, nameEn = p.Translations.FirstOrDefault(t => t.Locale == "en")?.Name, brand = p.Brand,
            description = p.Description, categorySlugs = slugs, minPrice = p.MinPrice, rating = p.RatingAverage,
            isActive = p.IsActive, createdAt = p.CreatedAt,
        };
        using var res = await http.PutAsJsonAsync($"{opt.Index}/_doc/{productId}", doc, ct);
        res.EnsureSuccessStatusCode();
    }

    public async Task RemoveAsync(Guid productId, CancellationToken ct)
    {
        using var res = await http.DeleteAsync($"{opt.Index}/_doc/{productId}", ct);
        if (res.StatusCode != HttpStatusCode.NotFound) res.EnsureSuccessStatusCode();
    }

    /// <summary>Tạo index với mapping nếu chưa có (gọi khi khởi động).</summary>
    public async Task EnsureIndexAsync(CancellationToken ct)
    {
        using var head = await http.SendAsync(new HttpRequestMessage(HttpMethod.Head, opt.Index), ct);
        if (head.IsSuccessStatusCode) return;
        var mapping = JsonNode.Parse("""
        { "mappings": { "properties": {
            "name": { "type": "text", "fields": { "keyword": { "type": "keyword" } } },
            "nameEn": { "type": "text" }, "description": { "type": "text" },
            "brand": { "type": "keyword" }, "categorySlugs": { "type": "keyword" },
            "minPrice": { "type": "double" }, "rating": { "type": "double" },
            "isActive": { "type": "boolean" }, "createdAt": { "type": "date" } } } }
        """);
        using var res = await http.PutAsJsonAsync(opt.Index, mapping, ct);
        res.EnsureSuccessStatusCode();
    }

    public async Task ReindexAllAsync(CancellationToken ct)
    {
        await EnsureIndexAsync(ct);
        foreach (var id in await db.Products.Select(p => p.Id).ToListAsync(ct))
            await IndexAsync(id, ct);
    }
}
