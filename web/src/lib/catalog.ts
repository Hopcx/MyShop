import "server-only";
import { cacheLife, cacheTag } from "next/cache";
import { cookies } from "next/headers";
import type { Category, Locale, Paged, ProductDetail, ProductSummary, Review } from "./types";

/** Server-side gọi API qua mạng nội bộ (service DNS trong K8s), client dùng NEXT_PUBLIC_API_URL. */
export const API_URL = process.env.API_INTERNAL_URL ?? process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:5080";

export class NotFoundError extends Error {}

async function getJson<T>(path: string): Promise<T> {
  const res = await fetch(`${API_URL}${path}`, { headers: { Accept: "application/json" } });
  if (res.status === 404) throw new NotFoundError(path);
  if (!res.ok) throw new Error(`API ${res.status} ${path}`);
  return res.json() as Promise<T>;
}

/** Tiền tệ khách chọn (cookie) — runtime data nên phải đọc trong <Suspense>. */
export async function getCurrency(): Promise<string> {
  const c = (await cookies()).get("currency")?.value?.toUpperCase();
  return c && /^[A-Z]{3}$/.test(c) ? c : "VND";
}

export interface ListParams {
  q?: string;
  category?: string;
  brand?: string;
  minPrice?: string;
  maxPrice?: string;
  sort?: string;
  page?: string;
}

export async function listProducts(params: ListParams, locale: Locale, currency: string, pageSize = 24) {
  "use cache";
  cacheLife("minutes");
  cacheTag("catalog");
  const qs = new URLSearchParams({ locale, currency, pageSize: String(pageSize) });
  for (const [k, v] of Object.entries(params)) if (v) qs.set(k, v);
  return getJson<Paged<ProductSummary>>(`/api/v1/products?${qs}`);
}

export async function getProduct(slug: string, locale: Locale, currency: string) {
  "use cache";
  cacheLife("minutes");
  cacheTag("catalog", `product:${slug}`);
  return getJson<ProductDetail>(`/api/v1/products/${encodeURIComponent(slug)}?locale=${locale}&currency=${currency}`);
}

export async function getCategories() {
  "use cache";
  cacheLife("hours");
  cacheTag("catalog");
  return getJson<Category[]>("/api/v1/categories");
}

export async function getBrands() {
  "use cache";
  cacheLife("hours");
  cacheTag("catalog");
  return getJson<string[]>("/api/v1/brands");
}

export async function getReviews(productId: string) {
  "use cache";
  cacheLife("minutes");
  cacheTag(`reviews:${productId}`);
  return getJson<Paged<Review>>(`/api/v1/products/${productId}/reviews`);
}
