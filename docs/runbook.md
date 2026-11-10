# Runbook vận hành

## Danh bạ & công cụ

| Thành phần | Nơi xem |
|---|---|
| Metrics / dashboard | Grafana → datasource Prometheus |
| Cảnh báo | Alertmanager (rule ở `deploy/monitoring/alerts.yml`) |
| Log | `kubectl -n myshop logs deploy/myshop-api` (hoặc Loki/ELK nếu có) |
| Health | `GET /health/live` (process), `GET /health/ready` (DB) |
| Spec API | `GET /openapi/v1.json` |

## Triển khai

1. Merge vào `main` → CI build, test, scan, deploy **staging** tự động.
2. Kiểm tra staging: k6 smoke, ZAP baseline, thử mua hàng 1 đơn.
3. Duyệt job `deploy-production` trên GitHub (environment `production`).
4. Theo dõi 15 phút: tỉ lệ 5xx, p95 catalog, outbox errors.

**Rollback**

```bash
kubectl -n myshop rollout undo deploy/myshop-api
```

```bash
kubectl -n myshop rollout undo deploy/myshop-web
```

Nếu dùng Argo Rollouts: `kubectl argo rollouts abort myshop-api -n myshop`.

**Migration DB**: schema phải tương thích ngược (expand → migrate → contract). Không xoá/đổi tên cột trong cùng release với code mới dùng nó. Chạy DDL trước khi deploy code.

## Sự cố thường gặp

### API 5xx tăng cao
1. Grafana: lỗi tập trung ở endpoint nào (`http_requests_received_total{code=~"5.."}` theo `controller/action`).
2. `kubectl logs` tìm exception. `/health/ready` đỏ → kiểm tra SQL Server (CPU, kết nối, blocking: `sp_who2`, `sys.dm_exec_requests`).
3. Vừa deploy? → rollback trước, điều tra sau.

### Độ trễ catalog cao
1. Tỉ lệ cache hit Redis (`INFO stats` → `keyspace_hits/misses`). Redis down → API vẫn chạy nhưng mọi request chạm DB.
2. Elasticsearch chậm/lỗi → log "Search backend failed, falling back to SQL". Kiểm tra `/_cluster/health`.
3. HPA đã scale tối đa? Tăng `maxReplicas` hoặc request CPU.
4. Truy vấn chậm: Query Store trên SQL Server; kiểm tra index `IX_Products_CategoryId_IsActive_MinPrice`.

### Outbox tồn đọng
1. `SELECT TOP 50 Type, Attempts, Error FROM OutboxMessages WHERE ProcessedAt IS NULL ORDER BY OccurredAt`.
2. Lỗi SMTP → kiểm tra credential/quota; lỗi ES → xem mục trên.
3. Sau khi sửa nguyên nhân, cho phép thử lại tin đã hết lượt:
   ```sql
   UPDATE OutboxMessages SET Attempts = 0, Error = NULL WHERE ProcessedAt IS NULL AND Attempts >= 5;
   ```

### Webhook thanh toán không tới / đơn kẹt ở `Created`
1. Kiểm tra `WebhookEvents` có bản ghi theo nguồn không. Không có → kiểm tra URL cấu hình bên cổng, WAF có chặn không, secret có khớp (`401`).
2. Có bản ghi nhưng đơn chưa đổi → log lỗi `amount_mismatch` / `invalid_transition`.
3. Đối soát thủ công với portal cổng thanh toán; dùng admin "Xác nhận" hoặc "Hủy".

### Oversell / tồn kho lệch
1. Xem ledger: `GET /api/v1/admin/inventory/{variantId}/movements`.
2. `Reserved` không giảm → đơn kẹt `Created`/`Confirmed` quá lâu: xử lý đơn hoặc hủy để giải phóng.
3. Điều chỉnh: `PUT /api/v1/admin/inventory/{variantId}` (ghi ledger `Adjust`).

### Nghi ngờ lộ refresh token / tấn công credential stuffing
1. Rate limit `auth` đang chặn (429)? Tăng cảnh báo WAF, bật challenge tại edge.
2. Thu hồi toàn bộ phiên của user: `UPDATE RefreshTokens SET RevokedAt = SYSUTCDATETIME() WHERE UserId = @id AND RevokedAt IS NULL`.
3. Lộ `Jwt:SigningKey` → xoay khóa (mọi access token mất hiệu lực sau tối đa 15'), thu hồi tất cả refresh token.

### API không phản hồi
1. `kubectl get pods` — CrashLoopBackOff? Xem log khởi động (thiếu `Jwt__SigningKey` ≥ 32 byte sẽ dừng ngay).
2. Probe fail do DB → xem mục 5xx.

## Bảo trì định kỳ

| Tần suất | Việc |
|---|---|
| Hằng ngày | Xem dashboard SLO, error budget còn lại |
| Hằng tuần | Cập nhật tỉ giá (`ExchangeRates`) nếu chưa tự động hoá; xem báo cáo Trivy/Dependabot |
| Hằng tháng | Thử restore backup vào môi trường riêng; dọn dữ liệu theo retention; review quyền admin |
| Hằng quý | Pen-test / ZAP full scan; diễn tập sự cố (game day); xoay vòng secret |
