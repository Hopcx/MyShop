import Link from "next/link";
import type { ListParams } from "@/lib/catalog";
import type { Dictionary } from "@/lib/i18n";
import type { Category, Locale } from "@/lib/types";

/** Bộ lọc dạng <form method="get"> — hoạt động cả khi chưa tải JS (progressive enhancement, tốt cho SEO/crawler). */
export function FilterPanel({
  params,
  categories,
  brands,
  locale,
  t,
}: {
  params: ListParams;
  categories: Category[];
  brands: string[];
  locale: Locale;
  t: Dictionary;
}) {
  const roots = categories.filter((c) => !c.parentId);
  return (
    <details className="group rounded-xl border border-zinc-200 bg-white p-4 lg:open:block" open>
      <summary className="cursor-pointer list-none font-semibold lg:pointer-events-none">
        {t.filter} <span className="text-zinc-400 lg:hidden">▾</span>
      </summary>
      <form method="get" action={`/${locale}/products`} className="mt-4 flex flex-col gap-4">
        {params.q && <input type="hidden" name="q" value={params.q} />}
        <input type="hidden" name="sort" value={params.sort ?? "newest"} />
        <fieldset>
          <legend className="mb-2 text-sm font-medium text-zinc-700">{t.categories}</legend>
          <select name="category" defaultValue={params.category ?? ""} className="min-h-11 w-full rounded-lg border border-zinc-300 px-2">
            <option value="">{t.allProducts}</option>
            {roots.map((r) => (
              <optgroup key={r.id} label={r.name}>
                <option value={r.slug}>{r.name}</option>
                {categories
                  .filter((c) => c.parentId === r.id)
                  .map((c) => (
                    <option key={c.id} value={c.slug}>
                      — {c.name}
                    </option>
                  ))}
              </optgroup>
            ))}
          </select>
        </fieldset>
        <fieldset>
          <legend className="mb-2 text-sm font-medium text-zinc-700">{t.brandLabel}</legend>
          <select name="brand" defaultValue={params.brand ?? ""} className="min-h-11 w-full rounded-lg border border-zinc-300 px-2">
            <option value="">—</option>
            {brands.map((b) => (
              <option key={b}>{b}</option>
            ))}
          </select>
        </fieldset>
        <div className="grid grid-cols-2 gap-2">
          <label className="text-sm text-zinc-700">
            {t.priceFrom} (₫)
            <input name="minPrice" type="number" min={0} step={1000} defaultValue={params.minPrice} inputMode="numeric"
              className="mt-1 min-h-11 w-full rounded-lg border border-zinc-300 px-2" />
          </label>
          <label className="text-sm text-zinc-700">
            {t.priceTo} (₫)
            <input name="maxPrice" type="number" min={0} step={1000} defaultValue={params.maxPrice} inputMode="numeric"
              className="mt-1 min-h-11 w-full rounded-lg border border-zinc-300 px-2" />
          </label>
        </div>
        <div className="flex gap-2">
          <button className="min-h-11 flex-1 rounded-lg bg-brand-600 px-3 font-semibold text-white hover:bg-brand-700">{t.applyFilter}</button>
          <Link href={`/${locale}/products`} className="flex min-h-11 items-center rounded-lg border border-zinc-300 px-3 text-sm">
            {t.clearFilter}
          </Link>
        </div>
      </form>
    </details>
  );
}

export function SortLinks({ params, locale, t }: { params: ListParams; locale: Locale; t: Dictionary }) {
  const options: [string, string][] = [
    ["newest", t.sortNewest],
    ["price_asc", t.sortPriceAsc],
    ["price_desc", t.sortPriceDesc],
    ["rating", t.sortRating],
  ];
  const current = params.sort ?? "newest";
  return (
    <nav aria-label={t.sort} className="flex gap-2 overflow-x-auto pb-1 text-sm">
      {options.map(([value, label]) => {
        const qs = new URLSearchParams(Object.entries({ ...params, sort: value, page: "" }).filter(([, v]) => v) as [string, string][]);
        return (
          <Link
            key={value}
            href={`/${locale}/products?${qs}`}
            aria-current={current === value ? "page" : undefined}
            className="whitespace-nowrap rounded-full border border-zinc-300 px-3 py-2 aria-[current=page]:border-brand-600 aria-[current=page]:bg-brand-50 aria-[current=page]:text-brand-700"
          >
            {label}
          </Link>
        );
      })}
    </nav>
  );
}
