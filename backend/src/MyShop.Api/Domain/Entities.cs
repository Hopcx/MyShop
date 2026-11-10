namespace MyShop.Api.Domain;

// ───────────── Catalog ─────────────
public class Category
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Slug { get; set; }
    public required string Name { get; set; }
    public Guid? ParentId { get; set; }
    public int SortOrder { get; set; }
}

public class Product
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Slug { get; set; }
    public required string Name { get; set; }
    public string Description { get; set; } = "";
    public string Brand { get; set; } = "";
    public Guid CategoryId { get; set; }
    public Category? Category { get; set; }
    /// <summary>Giá thấp nhất trong các variant, lưu theo tiền tệ gốc (VND) — denormalized để lọc/sắp xếp nhanh.</summary>
    public decimal MinPrice { get; set; }
    public double RatingAverage { get; set; }
    public int RatingCount { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public List<ProductVariant> Variants { get; set; } = [];
    public List<ProductImage> Images { get; set; } = [];
    public List<ProductTranslation> Translations { get; set; } = [];
}

public class ProductTranslation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProductId { get; set; }
    public required string Locale { get; set; }
    public required string Name { get; set; }
    public string Description { get; set; } = "";
}

public class ProductVariant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    public required string Sku { get; set; }
    public required string Name { get; set; }
    public string? Color { get; set; }
    public string? Size { get; set; }
    public decimal Price { get; set; }
    public decimal? CompareAtPrice { get; set; }
    public InventoryItem? Inventory { get; set; }
}

public class ProductImage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProductId { get; set; }
    public required string Url { get; set; }
    public string Alt { get; set; } = "";
    public int SortOrder { get; set; }
}

public class Review
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProductId { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public int Rating { get; set; }
    public string Comment { get; set; } = "";
    public bool VerifiedPurchase { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// ───────────── Inventory (ledger kiểu event-sourcing) ─────────────
public class InventoryItem
{
    public Guid VariantId { get; set; }
    public int OnHand { get; set; }
    public int Reserved { get; set; }
    public int Available => OnHand - Reserved;
}

public enum InventoryMovementType { Receive, Adjust, Reserve, Release, Ship }

public class InventoryMovement
{
    public long Id { get; set; }
    public Guid VariantId { get; set; }
    public InventoryMovementType Type { get; set; }
    public int Quantity { get; set; }
    public string Reference { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// ───────────── Identity ─────────────
public static class Roles
{
    public const string Customer = "Customer";
    public const string Admin = "Admin";
}

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Email { get; set; }
    public string PasswordHash { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Role { get; set; } = Roles.Customer;
    public string PreferredLocale { get; set; } = "vi";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public required string TokenHash { get; set; }
    public Guid FamilyId { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Address
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public required string FullName { get; set; }
    public required string Phone { get; set; }
    public required string Line1 { get; set; }
    public string Ward { get; set; } = "";
    public string District { get; set; } = "";
    public required string City { get; set; }
    public string Country { get; set; } = "VN";
    public bool IsDefault { get; set; }
}

public class WishlistItem
{
    public Guid UserId { get; set; }
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// ───────────── Cart ─────────────
public class Cart
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? UserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool CheckedOut { get; set; }
    public List<CartItem> Items { get; set; } = [];
}

public class CartItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CartId { get; set; }
    public Guid VariantId { get; set; }
    public ProductVariant? Variant { get; set; }
    public int Quantity { get; set; }
}

// ───────────── Orders ─────────────
/// <summary>Quy trình chuẩn 4 bước: Created → Confirmed → Shipping → Completed (+ Cancelled).</summary>
public enum OrderStatus { Created, Confirmed, Shipping, Completed, Cancelled }

public enum PaymentMethod { Cod, MockPay }

public class ShippingAddress
{
    public string FullName { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Line1 { get; set; } = "";
    public string Ward { get; set; } = "";
    public string District { get; set; } = "";
    public string City { get; set; } = "";
    public string Country { get; set; } = "VN";
}

public class Order
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Number { get; set; }
    public Guid? UserId { get; set; }
    public required string Email { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Created;
    public PaymentMethod PaymentMethod { get; set; }
    /// <summary>Tiền tệ hiển thị khách chọn + tỉ giá chốt tại thời điểm đặt hàng.</summary>
    public string Currency { get; set; } = "VND";
    public decimal ExchangeRate { get; set; } = 1m;
    // Các số tiền dưới đây lưu theo VND (tiền tệ gốc).
    public decimal Subtotal { get; set; }
    public decimal Discount { get; set; }
    public decimal ShippingFee { get; set; }
    public decimal Total { get; set; }
    public string? PromotionCode { get; set; }
    public ShippingAddress ShippingAddress { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public List<OrderItem> Items { get; set; } = [];
    public List<OrderStatusHistory> History { get; set; } = [];
}

public class OrderItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public Guid ProductId { get; set; }
    public Guid VariantId { get; set; }
    public required string Sku { get; set; }
    public required string ProductName { get; set; }
    public required string VariantName { get; set; }
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal => UnitPrice * Quantity;
}

public class OrderStatusHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public OrderStatus From { get; set; }
    public OrderStatus To { get; set; }
    public string Note { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// ───────────── Payments & Shipping ─────────────
public enum PaymentStatus { Pending, Succeeded, Failed, Refunded }

public class Payment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public PaymentMethod Method { get; set; }
    public decimal Amount { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public string? ProviderReference { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Lưu mọi webhook đã nhận để đảm bảo idempotency (EventId unique).</summary>
public class WebhookEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Source { get; set; }
    public required string EventId { get; set; }
    public string Payload { get; set; } = "";
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
}

public enum ShipmentStatus { Created, InTransit, Delivered, Failed }

public class Shipment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public required string Carrier { get; set; }
    public required string TrackingNumber { get; set; }
    public ShipmentStatus Status { get; set; } = ShipmentStatus.Created;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// ───────────── Promotions ─────────────
public enum PromotionType { Percent, Fixed }

public class Promotion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Code { get; set; }
    public string Description { get; set; } = "";
    public PromotionType Type { get; set; }
    public decimal Value { get; set; }
    public decimal MinSubtotal { get; set; }
    public decimal? MaxDiscount { get; set; }
    public DateTime StartsAt { get; set; } = DateTime.UtcNow;
    public DateTime? EndsAt { get; set; }
    public int? UsageLimit { get; set; }
    public int UsedCount { get; set; }
    public bool IsActive { get; set; } = true;
}

// ───────────── Notifications & Outbox ─────────────
public class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? UserId { get; set; }
    public required string Recipient { get; set; }
    public string Channel { get; set; } = "email";
    public required string Subject { get; set; }
    public string Body { get; set; } = "";
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Transactional outbox: sự kiện ghi cùng transaction với dữ liệu, được dispatcher xử lý bất đồng bộ.</summary>
public class OutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Type { get; set; }
    public required string Payload { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }
    public int Attempts { get; set; }
    public string? Error { get; set; }
}

public class ExchangeRate
{
    public required string Currency { get; set; }
    /// <summary>1 VND = Rate đơn vị tiền tệ này.</summary>
    public decimal Rate { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
