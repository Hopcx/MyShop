"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { Suspense } from "react";
import { useShop } from "@/components/ShopProvider";
import { currencies, type Dictionary } from "@/lib/i18n";
import type { Locale } from "@/lib/types";

export function Header({ locale, t }: { locale: Locale; t: Dictionary }) {
  const { cart, user, currency, setCurrency } = useShop();
  const router = useRouter();
  const otherLocale: Locale = locale === "vi" ? "en" : "vi";

  return (
    <header className="sticky top-0 z-30 border-b border-zinc-200 bg-white/95 backdrop-blur">
      <a href="#main" className="sr-only focus:not-sr-only focus:absolute focus:left-2 focus:top-2 focus:rounded focus:bg-white focus:p-2">
        {t.skipToContent}
      </a>
      <div className="mx-auto flex max-w-7xl flex-wrap items-center gap-3 px-4 py-3">
        <Link href={`/${locale}`} className="text-xl font-extrabold tracking-tight text-brand-700">
          {t.brand}
        </Link>

        <form
          role="search"
          className="order-last w-full sm:order-none sm:w-auto sm:flex-1"
          onSubmit={(e) => {
            e.preventDefault();
            const q = new FormData(e.currentTarget).get("q")?.toString().trim();
            router.push(`/${locale}/products${q ? `?q=${encodeURIComponent(q)}` : ""}`);
          }}
        >
          <label htmlFor="site-search" className="sr-only">
            {t.search}
          </label>
          <input
            id="site-search"
            name="q"
            type="search"
            placeholder={t.search}
            className="min-h-11 w-full rounded-full border border-zinc-300 bg-zinc-50 px-4 text-base outline-none focus:border-brand-600 focus:bg-white"
          />
        </form>

        <nav aria-label="Tiện ích" className="ml-auto flex items-center gap-1 text-sm sm:ml-0">
          <label className="sr-only" htmlFor="currency">
            Currency
          </label>
          <select
            id="currency"
            value={currency}
            onChange={(e) => setCurrency(e.target.value)}
            className="min-h-11 rounded-lg border border-zinc-200 bg-white px-2"
          >
            {currencies.map((c) => (
              <option key={c}>{c}</option>
            ))}
          </select>
          <Suspense fallback={<LocaleLink href={`/${otherLocale}`} locale={otherLocale} />}>
            <LocaleSwitch otherLocale={otherLocale} />
          </Suspense>
          {user?.role === "Admin" && (
            <Link href={`/${locale}/admin`} className="rounded-lg px-2 py-3 hover:bg-zinc-100">
              {t.admin}
            </Link>
          )}
          <Link href={`/${locale}/account`} className="rounded-lg px-2 py-3 hover:bg-zinc-100">
            {user ? user.fullName.split(" ").at(-1) : t.login}
          </Link>
          <Link href={`/${locale}/cart`} className="relative rounded-lg px-2 py-3 hover:bg-zinc-100" aria-label={`${t.cart} (${cart?.itemCount ?? 0})`}>
            <span aria-hidden>🛒</span>
            {!!cart?.itemCount && (
              <span className="absolute -right-0.5 top-1 min-w-5 rounded-full bg-brand-600 px-1 text-center text-xs font-bold text-white">
                {cart.itemCount}
              </span>
            )}
          </Link>
        </nav>
      </div>
    </header>
  );
}


/** usePathname là dữ liệu URL runtime → tách riêng trong <Suspense> để không chặn prerender static shell. */
function LocaleSwitch({ otherLocale }: { otherLocale: Locale }) {
  const pathname = usePathname();
  return <LocaleLink href={pathname.replace(/^\/(vi|en)(?=\/|$)/, `/${otherLocale}`)} locale={otherLocale} />;
}

function LocaleLink({ href, locale }: { href: string; locale: Locale }) {
  return (
    <Link href={href} hrefLang={locale} className="min-h-11 rounded-lg px-2 py-3 font-semibold uppercase hover:bg-zinc-100">
      {locale}
    </Link>
  );
}