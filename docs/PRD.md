# PRD — MyShop: Nền tảng bán hàng trực tuyến

| Mục | Nội dung |
|---|---|
| Phiên bản | 1.0 (MVP) |
| Ngày | 2026-11-10 |
| Thị trường mục tiêu | Việt Nam (VND), mở rộng quốc tế (USD/EUR, tiếng Anh) |
| Trạng thái | Đã có bản demo chạy được (xem README) |

## 1. Bối cảnh & mục tiêu

Xây dựng kênh bán hàng trực tiếp (D2C) để giảm phụ thuộc phí sàn, sở hữu dữ liệu khách hàng và tối ưu chuyển đổi.

**Mục tiêu sản phẩm**

1. Trải nghiệm mobile-first: >70% traffic TMĐT Việt Nam đến từ di động.
2. SEO: trang danh mục/sản phẩm render phía server (SSR/PPR), structured data, sitemap, hreflang.
3. Hiệu năng: API trang danh mục **p95 < 200ms**; LCP < 2.5s trên 4G.
4. Quy trình đơn hàng chuẩn 4 bước: **Tạo → Xác nhận → Giao → Hoàn thành** (+ Hủy).
5. An toàn & tuân thủ: PCI-DSS (SAQ A), bảo vệ dữ liệu cá nhân (NĐ 13/2023, Luật BVDLCN), OWASP Top 10.
6. Dễ mở rộng: modular monolith tách được thành microservice khi cần.

## 2. Nghiên cứu & lập kế hoạch

### 2.1 Phân tích cạnh tranh (định tính — cần xác nhận bằng khảo sát thực tế)

| Tiêu chí | Shopee | Tiki | Lazada | Hàm ý cho MyShop |
|---|---|---|---|---|
| Điểm mạnh | Giá rẻ, voucher dày đặc, livestream | Giao nhanh (TikiNOW), hàng chính hãng | Thương hiệu chính hãng (LazMall) | Cạnh tranh bằng trải nghiệm thương hiệu, không bằng giá |
| Checkout | 1 trang, ví ShopeePay mặc định | Nhiều bước, nhiều phương thức | Nhiều bước | Checkout ≤ 3 bước, cho phép **guest checkout** |
| Khuyến mãi | Voucher tầng (sàn + shop) | Coupon | Voucher | Engine mã giảm giá (%/cố định, trần, đơn tối thiểu, giới hạn lượt) |
| Tìm kiếm | Gợi ý, sửa lỗi chính tả | Lọc theo thuộc tính | Lọc sâu | Elasticsearch fuzzy + filter + sort |
| Điểm yếu | Hàng giả, trải nghiệm nhiễu | Danh mục hẹp hơn | UX chậm | Nội dung sạch, tốc độ, minh bạch tồn kho |

### 2.2 User journey

**Khách vãng lai (guest)**
```
Google/Ads ─► Trang danh mục (SSR) ─► Lọc/sắp xếp ─► Chi tiết SP (chọn màu/size)
   ─► Thêm giỏ (cartId lưu localStorage) ─► Checkout: email + địa chỉ ─► Chọn thanh toán
   ─► Xác nhận ─► Cổng thanh toán / COD ─► Trang thành công ─► Email xác nhận
   ─► Tra cứu đơn bằng (mã đơn + email)
```

**Thành viên (logged-in)**
```
Đăng nhập ─► Giỏ guest tự gộp vào giỏ tài khoản ─► Checkout điền sẵn email + địa chỉ mặc định
   ─► Theo dõi đơn, hủy đơn khi chưa xác nhận ─► Thông báo web + email theo trạng thái
   ─► Đánh giá sản phẩm (gắn nhãn "Đã mua hàng") ─► Wishlist, sổ địa chỉ
```

**Quản trị (admin)**: dashboard KPI → danh sách đơn theo trạng thái → Xác nhận/Giao/Hoàn thành/Hủy → import catalog CSV → điều chỉnh tồn kho → quản lý mã khuyến mãi.

### 2.3 KPI

| KPI | Định nghĩa | Nguồn trong hệ thống | Mục tiêu khởi điểm |
|---|---|---|---|
| CR (Conversion Rate) | Số đơn hợp lệ / số giỏ tạo mới (proxy cho session có ý định mua) | `GET /admin/reports/summary` → `conversionRate` | ≥ 2.5% |
| AOV (Average Order Value) | Doanh thu / số đơn hợp lệ | `averageOrderValue` | ≥ 450.000đ |
| LTV | Tổng chi tiêu trung bình mỗi khách (đơn Completed, theo email) | `averageCustomerLifetimeValue` | Theo dõi xu hướng |
| Tỉ lệ hủy | Đơn Cancelled / tổng đơn | `cancelledOrders` | < 5% |
| p95 latency catalog | Prometheus `http_request_duration_seconds` | Grafana | < 200ms |

> CR chuẩn nên dùng *session* từ analytics (GA4/PostHog). Bản MVP dùng "giỏ tạo mới" làm proxy; tích hợp analytics nằm trong roadmap.

**A/B testing**: dùng feature flag (vd. GrowthBook / Unleash / Vercel Flags) cho các biến thể: thứ tự phương thức thanh toán, vị trí nút "Mua ngay", ngưỡng freeship. Đo CR và AOV theo biến thể, tối thiểu 2 tuần hoặc đủ cỡ mẫu (power 80%, α=5%).

### 2.4 Phương thức thanh toán phổ biến tại Việt Nam

| Phương thức | Mức độ phổ biến | Trạng thái trong MyShop |
|---|---|---|
| COD | Rất cao, đặc biệt ngoài đô thị lớn | ✅ Hỗ trợ (thu tiền khi giao thành công) |
| Ví điện tử: MoMo, ZaloPay, ShopeePay | Cao, tăng nhanh | 🔌 Qua adapter webhook (`/webhooks/payments/{source}`) |
| QR ngân hàng (VietQR/Napas 247), VNPay-QR | Cao | 🔌 Như trên |
| Thẻ nội địa (Napas) / quốc tế (Visa/Master/JCB) | Trung bình | 🔌 Qua cổng (VNPay/OnePay/Stripe) — **không** lưu dữ liệu thẻ |
| MockPay | — | ✅ Cổng giả lập cho dev/demo |

### 2.5 Pháp lý (cần luật sư xác nhận trước khi go-live)

- **TMĐT**: NĐ 52/2013 & NĐ 85/2021 — thông báo website TMĐT bán hàng với Bộ Công Thương; công bố chính sách đổi trả, vận chuyển, bảo mật, thông tin người bán.
- **Thuế**: giá hiển thị đã gồm VAT; hóa đơn điện tử theo NĐ 123/2020 (tích hợp nhà cung cấp HĐĐT là hạng mục roadmap).
- **Bảo vệ người tiêu dùng**: Luật BVQLNTD 2023 — chính sách đổi trả rõ ràng (MVP: 7 ngày), lưu chứng từ giao dịch.
- **Dữ liệu cá nhân**: NĐ 13/2023 và Luật Bảo vệ dữ liệu cá nhân (hiệu lực 2026) — thu thập tối thiểu, có đồng ý, quyền xoá/truy cập; với khách EU áp dụng GDPR.
- **Thanh toán**: PCI-DSS — dùng hosted payment page/redirect để thuộc phạm vi SAQ A.

## 3. Phạm vi chức năng

### 3.1 Storefront

| Tính năng | Mô tả | Trạng thái |
|---|---|---|
| Danh sách sản phẩm | Lọc danh mục (gồm danh mục con), thương hiệu, khoảng giá; sắp xếp mới nhất/giá/đánh giá; phân trang; ảnh lazy-load | ✅ |
| Tìm kiếm | Full-text (Elasticsearch, fuzzy) với fallback SQL | ✅ |
| Chi tiết sản phẩm | Variant màu/size, giá theo variant, tồn kho thời gian thực, gallery + zoom, JSON-LD | ✅ |
| Giỏ hàng | Guest + thành viên, gộp giỏ khi đăng nhập, kiểm tra tồn kho | ✅ |
| Checkout nhiều bước | Thông tin → Thanh toán → Xác nhận; mã giảm giá; phí ship; báo giá trước | ✅ |
| Tài khoản | Đăng ký/đăng nhập, đơn hàng, hủy đơn, sổ địa chỉ, wishlist, thông báo | ✅ |
| Đánh giá | 1–5 sao, nhãn "Đã mua hàng", 1 đánh giá/khách/sản phẩm | ✅ |
| Đa ngôn ngữ | vi/en (URL `/vi`, `/en`, hreflang), nội dung sản phẩm dịch | ✅ |
| Đa tiền tệ | VND/USD/EUR; giá gốc VND, tỉ giá chốt tại thời điểm đặt | ✅ |
| Khuyến mãi động | Mã %/cố định, trần giảm, đơn tối thiểu, thời hạn, giới hạn lượt | ✅ |

### 3.2 Back-office

Dashboard KPI, xử lý đơn 4 bước, import CSV (ETL), tồn kho + lịch sử biến động, mã khuyến mãi — ✅. Quản lý nội dung (CMS), phân quyền chi tiết, hoàn tiền một phần — roadmap.

### 3.3 Ngoài phạm vi MVP / Roadmap

OAuth đăng nhập mạng xã hội (Google/Facebook/Zalo), adapter thật cho VNPay/MoMo/GHN/GHTK, hóa đơn điện tử, CRM sync (HubSpot/Salesforce), recommendation, PWA/push notification, flash sale có hàng đợi.

## 4. Wireframes (mobile-first, low-fi)

```
┌─────────────────────┐   ┌─────────────────────┐   ┌─────────────────────┐
│ MyShop  VND EN 👤 🛒│   │ ← Thời trang / Áo   │   │ Thanh toán          │
│ [🔍 Tìm sản phẩm…  ]│   │ ┌─────────────────┐ │   │ (1)━━(2)━━(3)       │
├─────────────────────┤   │ │     [ẢNH]       │ │   │ Email      [      ] │
│ ███ HERO BANNER ███ │   │ │   ● ○ ○  zoom   │ │   │ Họ tên     [      ] │
│ [Tất cả sản phẩm →] │   │ └─────────────────┘ │   │ SĐT        [      ] │
├─────────────────────┤   │ COOLMATE            │   │ Địa chỉ    [      ] │
│(Áo)(Quần)(Giày)(…) →│   │ Áo thun cotton      │   │ Tỉnh/TP    [      ] │
├─────────────────────┤   │ ★★★★☆ 4.6 (210)     │   │        [Tiếp tục]   │
│ Hàng mới về  Xem →  │   │ 159.000₫ ̶1̶9̶0̶.̶0̶0̶0̶ -16%│   ├─────────────────────┤
│ ┌──────┐ ┌──────┐   │   │ Màu: (Trắng)(Đen)   │   │ [MÃ KM      ][Áp dụng]│
│ │ ẢNH  │ │ ẢNH  │   │   │ Size: (S)(M)(L)(XL) │   │ Tạm tính   159.000₫ │
│ │Tên SP│ │Tên SP│   │   │ SL [1]  ● Còn hàng  │   │ Vận chuyển  30.000₫ │
│ │★ giá │ │★ giá │   │   ├─────────────────────┤   │ Tổng       189.000₫ │
│ └──────┘ └──────┘   │   │[Thêm vào giỏ][Mua ngay]│ └─────────────────────┘
└─────────────────────┘   └─────────────────────┘     (thanh CTA dính đáy)
     Trang chủ                Chi tiết SP                 Checkout bước 1
```

## 5. Yêu cầu phi chức năng

| Hạng mục | Yêu cầu |
|---|---|
| Hiệu năng | API catalog p95 < 200ms @ 1000 người dùng đồng thời; cache Redis + output cache; ảnh tối ưu qua `next/image` |
| Khả dụng | SLO 99.9%/30 ngày; ≥3 replica API trải trên nhiều zone; PDB |
| Bảo mật | OWASP ASVS L2; rate limiting; CSP; TLS 1.3; WAF; secrets ngoài mã nguồn |
| Accessibility | WCAG 2.1 AA: tương phản, focus visible, nhãn form, skip-link, target ≥ 44px, `prefers-reduced-motion` |
| SEO | SSR/PPR, canonical, hreflang, sitemap.xml, robots.txt, JSON-LD Product, noindex trang lọc sâu/giỏ/checkout |
| Quan sát | Metrics Prometheus, cảnh báo burn-rate SLO, log có che PII |

## 6. Tiêu chí nghiệm thu

- [x] Unit + integration test xanh trong CI (27 test: state machine, pricing, chữ ký webhook, luồng checkout đầy đủ, xoay vòng refresh token, phân quyền, import CSV, header bảo mật).
- [x] E2E thủ công trên viewport mobile: sản phẩm → giỏ → checkout 3 bước → thanh toán → đơn `Confirmed`.
- [ ] Load test k6 1000 VU trên staging đạt ngưỡng (p95 < 200ms, lỗi < 1%) — script có sẵn, cần môi trường staging.
- [ ] Security scan (Trivy + OWASP ZAP) không còn HIGH/CRITICAL — đã cấu hình trong CI.
- [ ] Accessibility audit (axe/Lighthouse ≥ 95) trên các trang chính.
