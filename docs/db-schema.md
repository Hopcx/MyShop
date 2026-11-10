# Mô hình dữ liệu

DDL đầy đủ cho SQL Server: [`db/schema.sqlserver.sql`](db/schema.sqlserver.sql) — sinh từ EF Core model bằng:

```bash
dotnet run --project backend/src/MyShop.Api -- --export-schema docs/db/schema.sqlserver.sql
```

(cần `Database__Provider=SqlServer` và `Jwt__SigningKey` trong biến môi trường; không cần kết nối DB).

## ER diagram

```mermaid
erDiagram
  Categories ||--o{ Categories : "parent"
  Categories ||--o{ Products : contains
  Products ||--o{ ProductVariants : has
  Products ||--o{ ProductImages : has
  Products ||--o{ ProductTranslations : "i18n"
  Products ||--o{ Reviews : receives
  ProductVariants ||--|| InventoryItems : stock
  ProductVariants ||--o{ InventoryMovements : ledger
  Users ||--o{ Addresses : owns
  Users ||--o{ RefreshTokens : sessions
  Users ||--o{ WishlistItems : saves
  Users ||--o{ Reviews : writes
  Users ||--o{ Carts : owns
  Carts ||--o{ CartItems : contains
  CartItems }o--|| ProductVariants : refers
  Users ||--o{ Orders : places
  Orders ||--o{ OrderItems : contains
  Orders ||--o{ OrderStatusHistory : audit
  Orders ||--o{ Payments : paid_by
  Orders ||--o{ Shipments : shipped_by
  Promotions ||--o{ Orders : "applied (by code)"

  Products { guid Id PK; string Slug UK; string Name; guid CategoryId FK; decimal MinPrice; float RatingAverage; bool IsActive }
  ProductVariants { guid Id PK; string Sku UK; string Color; string Size; decimal Price; decimal CompareAtPrice }
  InventoryItems { guid VariantId PK; int OnHand; int Reserved }
  InventoryMovements { bigint Id PK; guid VariantId; string Type; int Quantity; string Reference }
  Orders { guid Id PK; string Number UK; string Status; string Currency; decimal ExchangeRate; decimal Total }
  OrderItems { guid Id PK; guid VariantId; string Sku; string ProductName; decimal UnitPrice; int Quantity }
  Payments { guid Id PK; guid OrderId; string Method; string Status; decimal Amount; string ProviderReference }
  WebhookEvents { guid Id PK; string Source; string EventId; string Payload }
  OutboxMessages { guid Id PK; string Type; string Payload; datetime ProcessedAt; int Attempts }
```

## Quyết định thiết kế

- **Giá theo variant**, `Products.MinPrice` denormalized để lọc/sắp xếp theo giá có index (`IX_Products_CategoryId_IsActive_MinPrice`).
- **Tiền tệ**: mọi số tiền lưu bằng **VND** `decimal(18,2)`; đơn hàng chốt `Currency` + `ExchangeRate` lúc đặt để hiển thị/hoá đơn ổn định dù tỉ giá đổi.
- **Snapshot dòng đơn**: `OrderItems` copy SKU, tên, đơn giá → không phụ thuộc thay đổi catalog sau này. Sản phẩm bị "xoá" chỉ `IsActive = 0` (soft delete).
- **Tồn kho**: `Available = OnHand − Reserved`. Checkout tăng `Reserved`; giao hàng giảm cả `OnHand` và `Reserved`; huỷ giảm `Reserved`. Mỗi thay đổi có 1 dòng ledger.
- **Khoá Guid sinh phía client** (`ValueGeneratedNever`) để tạo ID trước khi lưu và tránh round-trip. Production cân nhắc **sequential GUID** (`NEWSEQUENTIALID`/UUIDv7) để giảm phân mảnh clustered index.
- **Unique constraint làm hàng rào idempotency**: `WebhookEvents(Source, EventId)`, `CartItems(CartId, VariantId)`, `Reviews(ProductId, UserId)`, `Orders.Number`, `ProductVariants.Sku`.
- Enum lưu dạng chuỗi (dễ đọc, an toàn khi thêm giá trị).

## Import catalog (ETL)

`POST /api/v1/admin/products/import` (multipart CSV, UTF-8, ≤ 20MB):

```csv
slug,name,brand,category,description,sku,variant_name,color,size,price,compare_at_price,stock,image_url
ao-polo,"Áo polo, cotton",Owen,ao,Mô tả,POLO-01,Trắng / M,Trắng,M,329000,,20,https://…/1.jpg
```

Mỗi dòng = 1 variant, gom theo `slug`; upsert sản phẩm/variant theo slug/SKU; điều chỉnh tồn kho ghi ledger; mỗi sản phẩm một transaction; trả về báo cáo `{created, updated, variantsUpserted, errors[]}`. Với file rất lớn (>100k dòng): chuyển sang job nền (Hangfire/Azure Functions) + `SqlBulkCopy` vào bảng staging rồi `MERGE`.

## Backup & retention

| Dữ liệu | Backup | Retention |
|---|---|---|
| SQL Server | Full hằng ngày + differential 6h + log backup 15' (RPO ≤ 15', RTO ≤ 1h); bản sao geo-redundant; **thử restore hằng tháng** | Backup 35 ngày; snapshot cuối tháng 12 tháng |
| Orders, Payments, OrderItems | — | ≥ 10 năm (chứng từ kế toán) |
| WebhookEvents | — | 180 ngày rồi xoá (job định kỳ) |
| OutboxMessages đã xử lý | — | 30 ngày |
| RefreshTokens hết hạn/thu hồi | — | 30 ngày sau `ExpiresAt` |
| Carts chưa checkout | — | 90 ngày không cập nhật |
| Notifications | — | 12 tháng |
| Người dùng yêu cầu xoá | Ẩn danh hoá `Users` (email → hash, tên → "Đã xoá"), giữ đơn hàng cho nghĩa vụ kế toán | — |
| Elasticsearch | Không backup — dựng lại từ SQL (`ReindexAllAsync`) | — |
| Redis | Không backup — cache | — |
