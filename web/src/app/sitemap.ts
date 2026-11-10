import type { MetadataRoute } from "next";
import { connection } from "next/server";
import { API_URL } from "@/lib/catalog";
import type { Paged, ProductSummary } from "@/lib/types";

const SITE = process.env.NEXT_PUBLIC_SITE_URL ?? "http://localhost:3000";

async function allProducts(): Promise<ProductSummary[]> {
  await connection();
  try {
    const res = await fetch(`${API_URL}/api/v1/products?pageSize=60&page=1`);
    if (!res.ok) return [];
    return ((await res.json()) as Paged<ProductSummary>).items;
  } catch {
    return []; // API lỗi → sitemap tối thiểu thay vì 500.
  }
}

export default async function sitemap(): Promise<MetadataRoute.Sitemap> {
  const products = await allProducts();
  const entries: MetadataRoute.Sitemap = ["vi", "en"].flatMap((l) => [
    { url: `${SITE}/${l}`, changeFrequency: "daily", priority: 1 },
    { url: `${SITE}/${l}/products`, changeFrequency: "daily", priority: 0.8 },
  ]);
  for (const p of products)
    entries.push({
      url: `${SITE}/vi/products/${p.slug}`,
      alternates: { languages: { vi: `${SITE}/vi/products/${p.slug}`, en: `${SITE}/en/products/${p.slug}` } },
      changeFrequency: "weekly",
      priority: 0.6,
    });
  return entries;
}
