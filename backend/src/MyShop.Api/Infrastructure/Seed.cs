using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MyShop.Api.Domain;
using MyShop.Api.Modules.Catalog;

namespace MyShop.Api.Infrastructure;

/// <summary>Dữ liệu mẫu cho dev/demo. Idempotent: chỉ seed khi bảng trống.</summary>
public static class Seed
{
    private record P(string Slug, string Name, string NameEn, string Brand, string Category, decimal Price, string[] Colors, string[] Sizes);

    private static readonly P[] Products =
    [
        new("ao-thun-cotton-basic", "Áo thun cotton basic", "Basic cotton T-shirt", "Coolmate", "ao", 159_000, ["Trắng", "Đen", "Xanh navy"], ["S", "M", "L", "XL"]),
        new("ao-polo-pique", "Áo polo vải pique", "Pique polo shirt", "Routine", "ao", 329_000, ["Trắng", "Xám"], ["M", "L", "XL"]),
        new("ao-so-mi-oxford", "Áo sơ mi Oxford dài tay", "Long-sleeve Oxford shirt", "Owen", "ao", 449_000, ["Xanh nhạt", "Trắng"], ["M", "L"]),
        new("ao-khoac-gio-2-lop", "Áo khoác gió 2 lớp", "Two-layer windbreaker", "Coolmate", "ao", 599_000, ["Đen", "Rêu"], ["M", "L", "XL"]),
        new("ao-hoodie-ni-bong", "Áo hoodie nỉ bông", "Fleece hoodie", "Routine", "ao", 489_000, ["Xám", "Đen", "Be"], ["S", "M", "L"]),
        new("quan-jean-slim", "Quần jean slim fit", "Slim fit jeans", "Levi's", "quan", 899_000, ["Xanh đậm", "Đen"], ["29", "30", "31", "32"]),
        new("quan-kaki-chino", "Quần kaki chino", "Chino trousers", "Owen", "quan", 499_000, ["Be", "Xanh navy"], ["29", "30", "31", "32"]),
        new("quan-short-the-thao", "Quần short thể thao", "Sport shorts", "Adidas", "quan", 390_000, ["Đen", "Xám"], ["S", "M", "L"]),
        new("quan-jogger-ni", "Quần jogger nỉ", "Fleece joggers", "Coolmate", "quan", 349_000, ["Đen", "Xám"], ["M", "L", "XL"]),
        new("giay-chay-bo-ultra", "Giày chạy bộ Ultra", "Ultra running shoes", "Adidas", "giay", 2_890_000, ["Đen", "Trắng"], ["40", "41", "42", "43"]),
        new("giay-sneaker-canvas", "Giày sneaker canvas", "Canvas sneakers", "Converse", "giay", 1_490_000, ["Trắng", "Đen", "Đỏ"], ["38", "39", "40", "41", "42"]),
        new("dep-quai-ngang", "Dép quai ngang", "Slide sandals", "Nike", "giay", 690_000, ["Đen"], ["39", "40", "41", "42"]),
        new("dien-thoai-galaxy-a55", "Điện thoại Galaxy A55 5G", "Galaxy A55 5G smartphone", "Samsung", "dien-thoai", 9_490_000, ["Xanh", "Đen"], ["128GB", "256GB"]),
        new("dien-thoai-iphone-16", "iPhone 16", "iPhone 16", "Apple", "dien-thoai", 22_990_000, ["Đen", "Trắng", "Hồng"], ["128GB", "256GB"]),
        new("dien-thoai-redmi-note-14", "Redmi Note 14", "Redmi Note 14", "Xiaomi", "dien-thoai", 5_290_000, ["Đen", "Xanh"], ["128GB"]),
        new("tai-nghe-bluetooth-anc", "Tai nghe Bluetooth chống ồn", "ANC Bluetooth headphones", "Sony", "phu-kien", 6_490_000, ["Đen", "Bạc"], []),
        new("tai-nghe-true-wireless", "Tai nghe true wireless", "True wireless earbuds", "Anker", "phu-kien", 1_190_000, ["Đen", "Trắng"], []),
        new("sac-du-phong-20000", "Sạc dự phòng 20.000mAh", "20,000mAh power bank", "Anker", "phu-kien", 790_000, ["Đen"], []),
        new("cap-sac-usb-c-100w", "Cáp sạc USB-C 100W", "100W USB-C cable", "Ugreen", "phu-kien", 190_000, ["Đen", "Trắng"], ["1m", "2m"]),
        new("dong-ho-thong-minh-fit", "Đồng hồ thông minh Fit", "Fit smartwatch", "Xiaomi", "phu-kien", 1_590_000, ["Đen", "Hồng"], []),
        new("binh-giu-nhiet-750ml", "Bình giữ nhiệt 750ml", "750ml insulated bottle", "Lock&Lock", "nha-cua", 359_000, ["Bạc", "Đen", "Xanh"], []),
        new("noi-chien-khong-dau-5l", "Nồi chiên không dầu 5L", "5L air fryer", "Philips", "nha-cua", 2_490_000, ["Đen"], []),
        new("den-ban-led-chong-can", "Đèn bàn LED chống cận", "Eye-care LED desk lamp", "Xiaomi", "nha-cua", 690_000, ["Trắng"], []),
        new("bo-chan-ga-cotton", "Bộ chăn ga cotton", "Cotton bedding set", "Everon", "nha-cua", 1_290_000, ["Trắng", "Xám", "Be"], ["1m6", "1m8"]),
    ];

    public static async Task RunAsync(IServiceProvider sp, CancellationToken ct = default)
    {
        var db = sp.GetRequiredService<ShopDbContext>();
        var config = sp.GetRequiredService<IConfiguration>();

        if (!await db.ExchangeRates.AnyAsync(ct))
        {
            db.ExchangeRates.AddRange(
                new ExchangeRate { Currency = "USD", Rate = 0.0000394m },
                new ExchangeRate { Currency = "EUR", Rate = 0.0000362m });
        }

        if (!await db.Categories.AnyAsync(ct))
        {
            var fashion = new Category { Slug = "thoi-trang", Name = "Thời trang", SortOrder = 1 };
            var tech = new Category { Slug = "cong-nghe", Name = "Công nghệ", SortOrder = 2 };
            var home = new Category { Slug = "nha-cua", Name = "Nhà cửa & Đời sống", SortOrder = 3 };
            db.Categories.AddRange(fashion, tech, home,
                new Category { Slug = "ao", Name = "Áo", ParentId = fashion.Id, SortOrder = 1 },
                new Category { Slug = "quan", Name = "Quần", ParentId = fashion.Id, SortOrder = 2 },
                new Category { Slug = "giay", Name = "Giày dép", ParentId = fashion.Id, SortOrder = 3 },
                new Category { Slug = "dien-thoai", Name = "Điện thoại", ParentId = tech.Id, SortOrder = 1 },
                new Category { Slug = "phu-kien", Name = "Phụ kiện", ParentId = tech.Id, SortOrder = 2 });
            await db.SaveChangesAsync(ct);
        }

        if (!await db.Products.AnyAsync(ct))
        {
            var cats = await db.Categories.ToDictionaryAsync(c => c.Slug, c => c.Id, ct);
            var rnd = new Random(42);
            var i = 0;
            foreach (var p in Products)
            {
                var product = new Product
                {
                    Slug = p.Slug, Name = p.Name, Brand = p.Brand, CategoryId = cats[p.Category],
                    Description = $"{p.Name} chính hãng {p.Brand}. Chất lượng cao, bảo hành đầy đủ, đổi trả trong 7 ngày.",
                    CreatedAt = DateTime.UtcNow.AddDays(-i++),
                    RatingAverage = Math.Round(3.8 + rnd.NextDouble() * 1.2, 1), RatingCount = rnd.Next(3, 400),
                };
                product.Translations.Add(new ProductTranslation
                {
                    Locale = "en", Name = p.NameEn,
                    Description = $"Genuine {p.Brand} {p.NameEn}. High quality, full warranty, 7-day returns.",
                });
                for (var img = 0; img < 3; img++)
                    product.Images.Add(new ProductImage { Url = $"https://picsum.photos/seed/{p.Slug}-{img}/800/800", Alt = p.Name, SortOrder = img });

                var colors = p.Colors.Length == 0 ? [null] : p.Colors.Cast<string?>().ToArray();
                var sizes = p.Sizes.Length == 0 ? [null] : p.Sizes.Cast<string?>().ToArray();
                var n = 0;
                foreach (var color in colors)
                foreach (var size in sizes)
                {
                    // Biến thể dung lượng lớn hơn đắt hơn — minh họa giá theo variant.
                    var price = p.Price + (size is "256GB" ? 3_000_000 : size is "2m" or "1m8" ? 50_000 : 0);
                    var variant = new ProductVariant
                    {
                        ProductId = product.Id, Sku = $"{p.Slug.ToUpperInvariant()[..Math.Min(12, p.Slug.Length)]}-{++n:D2}",
                        Name = string.Join(" / ", new[] { color, size }.OfType<string>().DefaultIfEmpty("Mặc định")),
                        Color = color, Size = size, Price = price,
                        CompareAtPrice = rnd.Next(3) == 0 ? Math.Round(price * 1.2m / 1000) * 1000 : null,
                    };
                    variant.Inventory = new InventoryItem { VariantId = variant.Id, OnHand = rnd.Next(0, 6) == 0 ? 0 : rnd.Next(5, 80) };
                    db.InventoryMovements.Add(new InventoryMovement
                    {
                        VariantId = variant.Id, Type = InventoryMovementType.Receive, Quantity = variant.Inventory.OnHand, Reference = "seed",
                    });
                    product.Variants.Add(variant);
                }
                CatalogService.RecomputeMinPrice(product);
                db.Products.Add(product);
            }
        }

        if (!await db.Promotions.AnyAsync(ct))
        {
            db.Promotions.AddRange(
                new Promotion { Code = "WELCOME10", Description = "Giảm 10% tối đa 100.000đ", Type = PromotionType.Percent, Value = 10, MaxDiscount = 100_000 },
                new Promotion { Code = "GIAM50K", Description = "Giảm 50.000đ cho đơn từ 300.000đ", Type = PromotionType.Fixed, Value = 50_000, MinSubtotal = 300_000 });
        }

        var adminEmail = config["Seed:AdminEmail"];
        var adminPassword = config["Seed:AdminPassword"];
        if (!string.IsNullOrEmpty(adminEmail) && !string.IsNullOrEmpty(adminPassword) && !await db.Users.AnyAsync(u => u.Email == adminEmail, ct))
        {
            var admin = new User { Email = adminEmail, FullName = "Quản trị viên", Role = Roles.Admin };
            admin.PasswordHash = sp.GetRequiredService<IPasswordHasher<User>>().HashPassword(admin, adminPassword);
            db.Users.Add(admin);
        }

        await db.SaveChangesAsync(ct);
    }
}
