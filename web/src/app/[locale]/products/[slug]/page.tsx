import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { Suspense } from "react";
import { Badge, Rating, Skeleton } from "@/components/atoms";
import { AddToCart, Gallery, ReviewForm } from "@/components/organisms/ProductInteractive";
import { getCurrency, getProduct, getReviews, NotFoundError } from "@/lib/catalog";
import { getDictionary, isLocale } from "@/lib/i18n";
import type { Locale } from "@/lib/types";

type Props = PageProps<"/[locale]/products/[slug]">;

async function load(locale: Locale, slug: string) {
  try {
    return await getProduct(slug, locale, "VND");
  } catch (e) {
    if (e instanceof NotFoundError) notFound();
    throw e;
  }
}

export async function generateMetadata({ params }: Props): Promise<Metadata> {
  const { locale: raw, slug } = await params;
  const locale: Locale = isLocale(raw) ? raw : "vi";
  const p = await load(locale, slug);
  return {
    title: p.name,
    description: p.description.slice(0, 160),
    alternates: { canonical: `/${locale}/products/${slug}`, languages: { vi: `/vi/products/${slug}`, en: `/en/products/${slug}` } },
    openGraph: { title: p.name, description: p.description.slice(0, 160), images: p.images.slice(0, 1).map((i) => i.url) },
  };
}

export default function ProductPage({ params }: Props) {
  return (
    <Suspense fallback={<DetailSkeleton />}>
      <Detail params={params} />
    </Suspense>
  );
}

async function Detail({ params }: Pick<Props, "params">) {
  const { locale: raw, slug } = await params;
  const locale: Locale = isLocale(raw) ? raw : "vi";
  const t = getDictionary(locale);
  const currency = await getCurrency();
  let product;
  try {
    product = await getProduct(slug, locale, currency);
  } catch (e) {
    if (e instanceof NotFoundError) notFound();
    throw e;
  }

  const minPrice = Math.min(...product.variants.map((v) => v.price));
  // Structured data (schema.org/Product) cho rich results trên Google.
  const jsonLd = {
    "@context": "https://schema.org",
    "@type": "Product",
    name: product.name,
    image: product.images.map((i) => i.url),
    description: product.description,
    sku: product.variants[0]?.sku,
    brand: { "@type": "Brand", name: product.brand },
    aggregateRating: product.ratingCount ? { "@type": "AggregateRating", ratingValue: product.rating, reviewCount: product.ratingCount } : undefined,
    offers: {
      "@type": "AggregateOffer",
      priceCurrency: product.currency,
      lowPrice: minPrice,
      offerCount: product.variants.length,
      availability: product.variants.some((v) => v.available > 0) ? "https://schema.org/InStock" : "https://schema.org/OutOfStock",
    },
  };

  return (
    <article className="flex flex-col gap-10">
      <script type="application/ld+json" dangerouslySetInnerHTML={{ __html: JSON.stringify(jsonLd).replace(/</g, "\\u003c") }} />
      <nav aria-label="Breadcrumb" className="text-sm text-zinc-500">
        <Link href={`/${locale}`}>{t.brand}</Link> / <Link href={`/${locale}/products?category=${product.categorySlug}`}>{product.categoryName}</Link>
      </nav>
      <div className="grid gap-8 lg:grid-cols-2">
        <Gallery images={product.images} name={product.name} />
        <div className="flex flex-col gap-4">
          <p className="text-sm uppercase tracking-wide text-zinc-500">{product.brand}</p>
          <h1 className="text-2xl font-bold sm:text-3xl">{product.name}</h1>
          <Rating value={product.rating} count={product.ratingCount} />
          <AddToCart product={product} locale={locale} t={t} />
          <section aria-labelledby="desc" className="border-t border-zinc-200 pt-4">
            <h2 id="desc" className="mb-2 font-semibold">
              {t.description}
            </h2>
            <p className="whitespace-pre-line text-zinc-700">{product.description}</p>
          </section>
        </div>
      </div>
      <section aria-labelledby="reviews" className="flex flex-col gap-4">
        <h2 id="reviews" className="text-xl font-bold">
          {t.reviews}
        </h2>
        <ReviewForm productId={product.id} t={t} />
        <Suspense fallback={<Skeleton className="h-24" />}>
          <ReviewList productId={product.id} locale={locale} />
        </Suspense>
      </section>
    </article>
  );
}

async function ReviewList({ productId, locale }: { productId: string; locale: Locale }) {
  const t = getDictionary(locale);
  const reviews = await getReviews(productId).catch(() => null);
  if (!reviews?.items.length) return null;
  return (
    <ul className="flex flex-col gap-3">
      {reviews.items.map((r) => (
        <li key={r.id} className="rounded-xl border border-zinc-200 bg-white p-4">
          <div className="flex flex-wrap items-center gap-2">
            <strong>{r.author}</strong>
            <Rating value={r.rating} />
            {r.verifiedPurchase && <Badge tone="green">{t.verified}</Badge>}
            <time className="ml-auto text-xs text-zinc-400" dateTime={r.createdAt}>
              {new Date(r.createdAt).toLocaleDateString(locale === "vi" ? "vi-VN" : "en-US")}
            </time>
          </div>
          <p className="mt-2 text-zinc-700">{r.comment}</p>
        </li>
      ))}
    </ul>
  );
}

function DetailSkeleton() {
  return (
    <div className="grid gap-8 lg:grid-cols-2">
      <Skeleton className="aspect-square" />
      <div className="flex flex-col gap-3">
        <Skeleton className="h-8 w-3/4" />
        <Skeleton className="h-6 w-1/3" />
        <Skeleton className="h-32" />
      </div>
    </div>
  );
}
