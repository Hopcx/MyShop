// Load test: 1000 người dùng đồng thời duyệt catalog + một phần thêm giỏ.
// Chạy: k6 run -e API=https://api.staging.example.com tests/load/k6-catalog.js
// Lưu ý: tăng RateLimit:GlobalPerMinute trên staging hoặc chạy từ nhiều IP, nếu không sẽ gặp 429.
import http from "k6/http";
import { check, sleep } from "k6";

const API = __ENV.API || "http://localhost:5080";

export const options = {
  scenarios: {
    browse: {
      executor: "ramping-vus",
      startVUs: 0,
      stages: [
        { duration: "1m", target: 200 },
        { duration: "2m", target: 1000 },
        { duration: "5m", target: 1000 },
        { duration: "1m", target: 0 },
      ],
    },
  },
  thresholds: {
    "http_req_duration{name:list}": ["p(95)<200"],
    "http_req_duration{name:detail}": ["p(95)<250"],
    http_req_failed: ["rate<0.01"],
  },
};

const categories = ["ao", "quan", "giay", "dien-thoai", "phu-kien", "nha-cua"];
const sorts = ["newest", "price_asc", "price_desc", "rating"];

export default function () {
  const cat = categories[Math.floor(Math.random() * categories.length)];
  const sort = sorts[Math.floor(Math.random() * sorts.length)];
  const list = http.get(`${API}/api/v1/products?category=${cat}&sort=${sort}`, { tags: { name: "list" } });
  check(list, { "list 200": (r) => r.status === 200 });

  const items = list.json("items") || [];
  if (items.length) {
    const p = items[Math.floor(Math.random() * items.length)];
    const detail = http.get(`${API}/api/v1/products/${p.slug}`, { tags: { name: "detail" } });
    check(detail, { "detail 200": (r) => r.status === 200 });

    // ~10% người dùng thêm vào giỏ.
    if (Math.random() < 0.1) {
      const variant = (detail.json("variants") || []).find((v) => v.available > 0);
      if (variant) {
        const cart = http.post(`${API}/api/v1/carts`, null, { tags: { name: "cart" } });
        if (cart.status === 201) {
          http.post(`${API}/api/v1/carts/${cart.json("id")}/items`, JSON.stringify({ variantId: variant.id, quantity: 1 }), {
            headers: { "Content-Type": "application/json" },
            tags: { name: "add" },
          });
        }
      }
    }
  }
  sleep(1 + Math.random() * 2);
}
