# MyShop — Nền tảng bán hàng trực tuyến

Storefront mobile-first chuẩn SEO (Next.js 16) + backend C# modular monolith (ASP.NET Core 10) với catalog, giỏ hàng, checkout nhiều bước, thanh toán qua webhook, quy trình đơn 4 bước, tồn kho chống oversell, khuyến mãi, báo cáo KPI, đa ngôn ngữ (vi/en) và đa tiền tệ (VND/USD/EUR).

## Cấu trúc

```
backend/            ASP.NET Core 10 API (modular monolith) + xUnit tests
  src/MyShop.Api/Modules/{Auth,Catalog,Cart,Orders,Payments,Inventory,Notifications,Reporting}
web/                Next.js 16 (App Router, Cache Components/PPR, Tailwind 4), atomic components
docs/               PRD, kiến trúc, DB schema (+ DDL SQL Server), OpenAPI spec, runbook
deploy/k8s/         Deployment/HPA/PDB/Ingress(TLS1.3+WAF)/NetworkPolicy, canary (Argo Rollouts)
deploy/monitoring/  Prometheus + cảnh báo SLO, Grafana provisioning
tests/load/         k6 — kịch bản 1000 người dùng đồng thời
.github/workflows/  CI/CD: test → image → scan → staging → duyệt → production
```

## Chạy nhanh (không cần Docker)

Yêu cầu: .NET SDK 10, Node 22.

```bash
dotnet run --project backend/src/MyShop.Api --launch-profile http --urls http://localhost:5080
```

```bash
npm --prefix web install
```

```bash
npm --prefix web run dev
```

Mở http://localhost:3000. Ở môi trường Development, API dùng SQLite (`myshop.dev.db`), tự tạo schema và seed 24 sản phẩm, 2 mã khuyến mãi (`WELCOME10`, `GIAM50K`) và một tài khoản admin — email/mật khẩu admin dev nằm trong `backend/src/MyShop.Api/appsettings.Development.json` (`Seed:AdminEmail`, `Seed:AdminPassword`). Thanh toán dùng cổng giả lập **MockPay**.

## Chạy đầy đủ với Docker (SQL Server + Redis + Elasticsearch + Prometheus + Grafana)

```bash
cp .env.example .env
```

Điền các secret trong `.env`, rồi:

```bash
docker compose up -d --build
```

Web: http://localhost:3000 · API: http://localhost:5080/openapi/v1.json · Prometheus: http://localhost:9090 · Grafana: http://localhost:3001

## Kiểm thử

```bash
dotnet test backend
```

```bash
npm --prefix web run lint
```

```bash
k6 run -e API=http://localhost:5080 tests/load/k6-catalog.js
```

## Tài liệu

- [PRD](docs/PRD.md) — mục tiêu, nghiên cứu, user journey, KPI, wireframe, tiêu chí nghiệm thu
- [Kiến trúc](docs/architecture.md) — module, luồng checkout/webhook, cache, bảo mật & tuân thủ, triển khai
- [Mô hình dữ liệu](docs/db-schema.md) — ER, quyết định thiết kế, ETL, backup & retention · [DDL](docs/db/schema.sqlserver.sql)
- [OpenAPI spec](docs/api/openapi.json)
- [Runbook](docs/runbook.md)
