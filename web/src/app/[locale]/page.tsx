import Link from "next/link";
import { connection } from "next/server";
import { Suspense } from "react";
import { ProductCard, ProductGrid, ProductGridSkeleton } from "@/components/molecules/ProductCard";
import { getCategories, getCurrency, listProducts } from "@/lib/catalog";
import { getDictionary, isLocale } from "@/lib/i18n";
import type { Locale } from "@/lib/types";

export default async function Home({ params }: PageProps<"/[locale]">) {
  const { locale: raw } = await params;
  const locale: Locale = isLocale(raw) ? raw : "vi";
  const t = getDictionary(locale);

  return (
    <div className="flex flex-col gap-8">
      <section className="rounded-2xl bg-gradient-to-br from-brand-700 to-brand-600 px-6 py-10 text-white sm:px-10 sm:py-14">
        <h1 className="text-3xl font-extrabold sm:text-4xl">{t.tagline}</h1>
        <p className="mt-2 max-w-xl text-brand-100">{t.freeShipNote}</p>
        <Link href={`/${locale}/products`} className="mt-6 inline-flex min-h-11 items-center rounded-lg bg-white px-5 font-semibold text-brand-700">
          {t.allProducts} →
        </Link>
      </section>

      <Suspense fallback={<div className="h-12" />}>
        <CategoryChips locale={locale} title={t.categories} />
      </Suspense>

      <section aria-labelledby="new-heading" className="flex flex-col gap-4">
        <div className="flex items-baseline justify-between">
          <h2 id="new-heading" className="text-xl font-bold">
            {t.newArrivals}
          </h2>
          <Link href={`/${locale}/products`} className="text-sm font-semibold text-brand-700">
            {t.viewAll}
          </Link>
        </div>
        <Suspense fallback={<ProductGridSkeleton />}>
          <NewArrivals locale={locale} />
        </Suspense>
      </section>
    </div>
  );
}

async function CategoryChips({ locale, title }: { locale: Locale; title: string }) {
  await connection(); // Lấy dữ liệu lúc request (không phụ thuộc API khi build).
  const categories = await getCategories().catch(() => []);
  return (
    <nav aria-label={title} className="flex gap-2 overflow-x-auto pb-1">
      {categories.map((c) => (
        <Link key={c.id} href={`/${locale}/products?category=${c.slug}`}
          className="whitespace-nowrap rounded-full border border-zinc-200 bg-white px-4 py-2 text-sm font-medium hover:border-brand-600">
          {c.name}
        </Link>
      ))}
    </nav>
  );
}

async function NewArrivals({ locale }: { locale: Locale }) {
  const t = getDictionary(locale);
  const currency = await getCurrency();
  const data = await listProducts({ sort: "newest" }, locale, currency, 8);
  return (
    <ProductGrid>
      {data.items.map((p, i) => (
        <ProductCard key={p.id} p={p} locale={locale} t={t} priority={i < 2} />
      ))}
    </ProductGrid>
  );
}
