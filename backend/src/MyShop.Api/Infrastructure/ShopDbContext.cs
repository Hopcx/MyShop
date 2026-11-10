using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MyShop.Api.Domain;

namespace MyShop.Api.Infrastructure;

public class ShopDbContext(DbContextOptions<ShopDbContext> options) : DbContext(options)
{
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductTranslation> ProductTranslations => Set<ProductTranslation>();
    public DbSet<ProductVariant> Variants => Set<ProductVariant>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<InventoryItem> Inventory => Set<InventoryItem>();
    public DbSet<InventoryMovement> InventoryMovements => Set<InventoryMovement>();
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Address> Addresses => Set<Address>();
    public DbSet<WishlistItem> Wishlist => Set<WishlistItem>();
    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderStatusHistory> OrderHistory => Set<OrderStatusHistory>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<WebhookEvent> WebhookEvents => Set<WebhookEvent>();
    public DbSet<Shipment> Shipments => Set<Shipment>();
    public DbSet<Promotion> Promotions => Set<Promotion>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();
    public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();

    /// <summary>Ghi sự kiện vào outbox — sẽ được commit cùng transaction với SaveChanges.</summary>
    public void Enqueue(string type, object payload) =>
        Outbox.Add(new OutboxMessage { Type = type, Payload = JsonSerializer.Serialize(payload) });

    protected override void ConfigureConventions(ModelConfigurationBuilder b)
    {
        if (Database.IsSqlite())
        {
            // SQLite không hỗ trợ so sánh/sắp xếp decimal phía server → lưu dạng REAL khi dev/test.
            b.Properties<decimal>().HaveConversion<double>();
        }
        else
        {
            b.Properties<decimal>().HavePrecision(18, 2);
        }
        b.Properties<OrderStatus>().HaveConversion<string>().HaveMaxLength(20);
        b.Properties<PaymentStatus>().HaveConversion<string>().HaveMaxLength(20);
        b.Properties<PaymentMethod>().HaveConversion<string>().HaveMaxLength(20);
        b.Properties<ShipmentStatus>().HaveConversion<string>().HaveMaxLength(20);
        b.Properties<PromotionType>().HaveConversion<string>().HaveMaxLength(20);
        b.Properties<InventoryMovementType>().HaveConversion<string>().HaveMaxLength(20);
    }

    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<Category>(e =>
        {
            e.HasIndex(x => x.Slug).IsUnique();
            e.Property(x => x.Slug).HasMaxLength(120);
            e.Property(x => x.Name).HasMaxLength(200);
        });

        m.Entity<Product>(e =>
        {
            e.HasIndex(x => x.Slug).IsUnique();
            e.HasIndex(x => new { x.CategoryId, x.IsActive, x.MinPrice });
            e.HasIndex(x => x.CreatedAt);
            e.Property(x => x.Slug).HasMaxLength(160);
            e.Property(x => x.Name).HasMaxLength(300);
            e.Property(x => x.Brand).HasMaxLength(120);
            e.Property(x => x.Description).HasMaxLength(8000);
            e.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Variants).WithOne(v => v.Product).HasForeignKey(v => v.ProductId);
            e.HasMany(x => x.Images).WithOne().HasForeignKey(i => i.ProductId);
            e.HasMany(x => x.Translations).WithOne().HasForeignKey(t => t.ProductId);
        });

        m.Entity<ProductTranslation>(e =>
        {
            e.HasIndex(x => new { x.ProductId, x.Locale }).IsUnique();
            e.Property(x => x.Locale).HasMaxLength(10);
            e.Property(x => x.Name).HasMaxLength(300);
            e.Property(x => x.Description).HasMaxLength(8000);
        });

        m.Entity<ProductVariant>(e =>
        {
            e.HasIndex(x => x.Sku).IsUnique();
            e.Property(x => x.Sku).HasMaxLength(64);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Color).HasMaxLength(50);
            e.Property(x => x.Size).HasMaxLength(20);
            e.HasOne(x => x.Inventory).WithOne().HasForeignKey<InventoryItem>(i => i.VariantId);
        });

        m.Entity<ProductImage>(e => e.Property(x => x.Url).HasMaxLength(1000));

        m.Entity<Review>(e =>
        {
            e.HasIndex(x => new { x.ProductId, x.CreatedAt });
            e.HasIndex(x => new { x.ProductId, x.UserId }).IsUnique();
            e.Property(x => x.Comment).HasMaxLength(2000);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
        });

        m.Entity<InventoryItem>(e => e.HasKey(x => x.VariantId));
        m.Entity<InventoryMovement>(e =>
        {
            e.HasIndex(x => new { x.VariantId, x.CreatedAt });
            e.Property(x => x.Reference).HasMaxLength(100);
        });

        m.Entity<User>(e =>
        {
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Email).HasMaxLength(256);
            e.Property(x => x.FullName).HasMaxLength(200);
            e.Property(x => x.Role).HasMaxLength(20);
            e.Property(x => x.PreferredLocale).HasMaxLength(10);
        });

        m.Entity<RefreshToken>(e =>
        {
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.FamilyId);
            e.Property(x => x.TokenHash).HasMaxLength(128);
        });

        m.Entity<Address>(e => e.HasIndex(x => x.UserId));
        m.Entity<WishlistItem>(e =>
        {
            e.HasKey(x => new { x.UserId, x.ProductId });
            e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId);
        });

        m.Entity<Cart>(e =>
        {
            e.HasIndex(x => x.UserId);
            e.HasMany(x => x.Items).WithOne().HasForeignKey(i => i.CartId).OnDelete(DeleteBehavior.Cascade);
        });
        m.Entity<CartItem>(e =>
        {
            e.HasIndex(x => new { x.CartId, x.VariantId }).IsUnique();
            e.HasOne(x => x.Variant).WithMany().HasForeignKey(x => x.VariantId);
        });

        m.Entity<Order>(e =>
        {
            e.HasIndex(x => x.Number).IsUnique();
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
            e.HasIndex(x => new { x.Status, x.CreatedAt });
            e.Property(x => x.Number).HasMaxLength(32);
            e.Property(x => x.Email).HasMaxLength(256);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.ExchangeRate).HasPrecision(18, 8);
            e.Property(x => x.PromotionCode).HasMaxLength(50);
            e.OwnsOne(x => x.ShippingAddress);
            e.HasMany(x => x.Items).WithOne().HasForeignKey(i => i.OrderId);
            e.HasMany(x => x.History).WithOne().HasForeignKey(h => h.OrderId);
        });
        m.Entity<OrderItem>(e => e.Ignore(x => x.LineTotal));

        m.Entity<Payment>(e =>
        {
            e.HasIndex(x => x.OrderId);
            e.HasIndex(x => x.ProviderReference);
        });

        m.Entity<WebhookEvent>(e =>
        {
            e.HasIndex(x => new { x.Source, x.EventId }).IsUnique();
            e.Property(x => x.Source).HasMaxLength(50);
            e.Property(x => x.EventId).HasMaxLength(100);
        });

        m.Entity<Shipment>(e =>
        {
            e.HasIndex(x => x.TrackingNumber).IsUnique();
            e.HasIndex(x => x.OrderId);
        });

        m.Entity<Promotion>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(50);
        });

        m.Entity<Notification>(e => e.HasIndex(x => new { x.UserId, x.CreatedAt }));
        m.Entity<OutboxMessage>(e =>
        {
            e.HasIndex(x => new { x.ProcessedAt, x.OccurredAt });
            e.Property(x => x.Type).HasMaxLength(100);
        });
        m.Entity<ExchangeRate>(e =>
        {
            e.HasKey(x => x.Currency);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.Rate).HasPrecision(18, 8);
        });

        // Id Guid được sinh phía client → khai báo ValueGeneratedNever để entity con thêm qua navigation
        // (vd. order.History.Add) luôn được track là Added thay vì Modified.
        foreach (var entity in m.Model.GetEntityTypes())
        foreach (var key in entity.FindPrimaryKey()?.Properties ?? [])
            if (key.ClrType == typeof(Guid) && key.Name == "Id")
                key.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
    }
}
