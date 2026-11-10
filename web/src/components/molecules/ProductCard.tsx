import Image from "next/image";
import Link from "next/link";
import { Badge, Price, Rating, Skeleton } from "@/components/atoms";
import type { Dictionary } from "@/lib/i18n";
import type { Locale, ProductSummary } from "@/lib/types";

export function ProductCard({ p, locale, t, priority }: { p: ProductSummary; locale: Locale; t: Dictionary; priority?: boolean }) {
  return (
    <article className="group relative flex flex-col overflow-hidden rounded-xl border border-zinc-200 bg-white transition hover:shadow-md">
      <div className="relative aspect-square bg-zinc-100">
        {p.image && (
          <Image
            src={p.image}
            alt={p.name}
            fill
            sizes="(max-width: 640px) 50vw, (max-width: 1024px) 33vw, 25vw"
            className="object-cover transition group-hover:scale-105"
            priority={priority}
            loading={priority ? undefined : "lazy"}
          />
        )}
        {!p.inStock && (
          <span className="absolute left-2 top-2">
            <Badge>{t.outOfStock}</Badge>
          </span>
        )}
      </div>
      <div className="flex flex-1 flex-col gap-1 p-3">
        <p className="text-xs uppercase tracking-wide text-zinc-500">{p.brand}</p>
        <h3 className="line-clamp-2 text-sm font-medium text-zinc-900">
          <Link href={`/${locale}/products/${p.slug}`} className="after:absolute after:inset-0 focus-visible:outline-none">
            {p.name}
          </Link>
        </h3>
        <Rating value={p.rating} count={p.ratingCount} />
        <div className="mt-auto pt-1">
          <Price amount={p.price} compareAt={p.compareAtPrice} currency={p.currency} locale={locale} />
        </div>
      </div>
    </article>
  );
}

export function ProductGrid({ children }: { children: React.ReactNode }) {
  return <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4 lg:gap-4">{children}</div>;
}

export function ProductGridSkeleton({ count = 8 }: { count?: number }) {
  return (
    <ProductGrid>
      {Array.from({ length: count }, (_, i) => (
        <div key={i} className="flex flex-col gap-2">
          <Skeleton className="aspect-square" />
          <Skeleton className="h-4 w-3/4" />
          <Skeleton className="h-4 w-1/2" />
        </div>
      ))}
    </ProductGrid>
  );
}
