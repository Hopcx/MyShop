"use client";

import Image from "next/image";
import Link from "next/link";
import { useState } from "react";
import { Button, Spinner } from "@/components/atoms";
import { useShop } from "@/components/ShopProvider";
import { ApiError } from "@/lib/client-api";
import { formatMoney, type Dictionary } from "@/lib/i18n";
import type { Locale } from "@/lib/types";

export function CartView({ locale, t }: { locale: Locale; t: Dictionary }) {
  const { cart, ready, updateLine } = useShop();
  const [error, setError] = useState<string | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);

  if (!ready) return <Spinner label={t.loading} />;
  if (!cart || cart.items.length === 0)
    return (
      <div className="flex flex-col items-center gap-4 py-16 text-center">
        <p className="text-lg text-zinc-600">{t.emptyCart}</p>
        <Link href={`/${locale}/products`} className="font-semibold text-brand-700">
          {t.continueShopping} →
        </Link>
      </div>
    );

  async function change(id: string, qty: number) {
    setBusyId(id);
    setError(null);
    try {
      await updateLine(id, qty);
    } catch (e) {
      setError(e instanceof ApiError ? e.message : t.error);
    } finally {
      setBusyId(null);
    }
  }

  return (
    <div className="grid gap-6 lg:grid-cols-[1fr_340px]">
      <section aria-labelledby="cart-title">
        <h1 id="cart-title" className="mb-4 text-2xl font-bold">
          {t.cart} ({cart.itemCount})
        </h1>
        {error && (
          <p role="alert" className="mb-3 rounded-lg bg-red-50 p-3 text-red-700">
            {error}
          </p>
        )}
        <ul className="flex flex-col gap-3">
          {cart.items.map((line) => (
            <li key={line.id} className="flex gap-3 rounded-xl border border-zinc-200 bg-white p-3" aria-busy={busyId === line.id}>
              <div className="relative size-20 shrink-0 overflow-hidden rounded-lg bg-zinc-100">
                {line.image && <Image src={line.image} alt="" fill sizes="80px" className="object-cover" />}
              </div>
              <div className="flex min-w-0 flex-1 flex-col gap-1">
                <Link href={`/${locale}/products/${line.productSlug}`} className="line-clamp-2 font-medium">
                  {line.productName}
                </Link>
                <p className="text-sm text-zinc-500">{line.variantName}</p>
                <div className="mt-auto flex flex-wrap items-center gap-3">
                  <div className="flex items-center rounded-lg border border-zinc-300">
                    <button className="size-11" aria-label="-" onClick={() => change(line.id, line.quantity - 1)} disabled={busyId === line.id}>
                      −
                    </button>
                    <span className="w-8 text-center" aria-label={t.quantity}>
                      {line.quantity}
                    </span>
                    <button
                      className="size-11 disabled:text-zinc-300"
                      aria-label="+"
                      onClick={() => change(line.id, line.quantity + 1)}
                      disabled={busyId === line.id || line.quantity >= line.available}
                    >
                      +
                    </button>
                  </div>
                  <button className="text-sm text-zinc-500 underline" onClick={() => change(line.id, 0)}>
                    {t.remove}
                  </button>
                  <span className="ml-auto font-semibold">{formatMoney(line.lineTotal, cart.currency, locale)}</span>
                </div>
              </div>
            </li>
          ))}
        </ul>
      </section>
      <aside className="flex h-fit flex-col gap-3 rounded-xl border border-zinc-200 bg-white p-4 lg:sticky lg:top-24">
        <div className="flex justify-between">
          <span>{t.subtotal}</span>
          <strong>{formatMoney(cart.subtotal, cart.currency, locale)}</strong>
        </div>
        <p className="text-sm text-zinc-500">{t.freeShipNote}</p>
        <Link href={`/${locale}/checkout`} className="block">
          <Button className="w-full">{t.checkout}</Button>
        </Link>
      </aside>
    </div>
  );
}
