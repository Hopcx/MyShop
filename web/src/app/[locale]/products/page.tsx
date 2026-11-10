import type { Metadata } from "next";
import Link from "next/link";
import { Suspense } from "react";
import { ProductCard, ProductGrid, ProductGridSkeleton } from "@/components/molecules/ProductCard";
import { FilterPanel, SortLinks } from "@/components/organisms/FilterPanel";
import { getBrands, getCategories, getCurrency, listProducts, type ListParams } from "@/lib/catalog";
import { getDictionary, isLocale } from "@/lib/i18n";
import type { Locale } from "@/lib/types";

const KEYS = ["q", "category", "brand", "minPrice", "maxPrice", "sort", "page"] as const;

function pick(sp: Record<string, string | string[] | undefined>): ListParams {
  const out: ListParams = {};
  for (const k of KEYS) {
    const v = sp[k];
    const s = Array.isArray(v) ? v[0] : v;
    if (s) out[k] = s.slice(0, 100);
  }
  return out;
}

export async function generateMetadata({ params, searchParams }: PageProps<"/[locale]/products">): Promise<Metadata> {
  const [{ locale }, sp] = await Promise.all([params, searchParams]);
  const t = getDictionary(isLocale(locale) ? locale : "vi");
  const p = pick(sp);
  return {
    title: p.q ? `"${p.q}"` : t.allProducts,
    alternates: { canonical: `/${locale}/products${p.category ? `?category=${p.category}` : ""}` },
    // Trang lọc/tìm kiếm có tham số sâu → noindex tránh trùng lặp nội dung.
    robots: p.q || p.brand || p.minPrice || p.maxPrice || p.page ? { index: false, follow: true } : undefined,
  };
}

export default function ProductsPage({ params, searchParams }: PageProps<"/[locale]/products">) {
  return (
    <Suspense fallback={<ProductGridSkeleton count={12} />}>
      <Listing params={params} searchParams={searchParams} />
    </Suspense>
  );
}

async function Listing({ params, searchParams }: Pick<PageProps<"/[locale]/products">, "params" | "searchParams">) {
  const [{ locale: raw }, sp] = await Promise.all([params, searchParams]);
  const locale: Locale = isLocale(raw) ? raw : "vi";
  const t = getDictionary(locale);
  const p = pick(sp);
  const currency = await getCurrency();
  const [data, categories, brands] = await Promise.all([
    listProducts(p, locale, currency),
    getCategories().catch(() => []),
    getBrands().catch(() => []),
  ]);
  const page = data.page;
  const pageHref = (n: number) => {
    const qs = new URLSearchParams(Object.entries({ ...p, page: String(n) }).filter(([, v]) => v) as [string, string][]);
    return `/${locale}/products?${qs}`;
  };
  const title = p.q ? `"${p.q}"` : (categories.find((c) => c.slug === p.category)?.name ?? t.allProducts);

  return (
    <div className="grid gap-6 lg:grid-cols-[260px_1fr]">
      <aside>
        <FilterPanel params={p} categories={categories} brands={brands} locale={locale} t={t} />
      </aside>
      <section className="flex min-w-0 flex-col gap-4" aria-labelledby="list-title">
        <div className="flex flex-wrap items-baseline justify-between gap-2">
          <h1 id="list-title" className="text-2xl font-bold">
            {title}
          </h1>
          <p className="text-sm text-zinc-500" aria-live="polite">
            {data.total} {t.results}
          </p>
        </div>
        <SortLinks params={p} locale={locale} t={t} />
        {data.items.length === 0 ? (
          <p className="rounded-xl bg-white p-8 text-center text-zinc-500">{t.noResults}</p>
        ) : (
          <ProductGrid>
            {data.items.map((item, i) => (
              <ProductCard key={item.id} p={item} locale={locale} t={t} priority={i < 4} />
            ))}
          </ProductGrid>
        )}
        {data.totalPages > 1 && (
          <nav aria-label="Pagination" className="flex justify-center gap-2">
            {page > 1 && (
              <Link rel="prev" href={pageHref(page - 1)} className="min-h-11 rounded-lg border border-zinc-300 bg-white px-4 py-2.5">
                ←
              </Link>
            )}
            <span className="px-3 py-2.5 text-sm">
              {page} / {data.totalPages}
            </span>
            {page < data.totalPages && (
              <Link rel="next" href={pageHref(page + 1)} className="min-h-11 rounded-lg border border-zinc-300 bg-white px-4 py-2.5">
                →
              </Link>
            )}
          </nav>
        )}
      </section>
    </div>
  );
}
