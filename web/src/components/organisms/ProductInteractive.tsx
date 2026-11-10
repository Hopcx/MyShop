"use client";

import Image from "next/image";
import { useRouter } from "next/navigation";
import { useMemo, useState } from "react";
import { Badge, Button, Price } from "@/components/atoms";
import { useShop } from "@/components/ShopProvider";
import { ApiError, api } from "@/lib/client-api";
import type { Dictionary } from "@/lib/i18n";
import type { Locale, ProductDetail, Review } from "@/lib/types";

/** Gallery có thumbnail + zoom theo vị trí chuột (desktop) / chạm để phóng to (mobile). */
export function Gallery({ images, name }: { images: { url: string; alt: string }[]; name: string }) {
  const [active, setActive] = useState(0);
  const [zoom, setZoom] = useState<{ x: number; y: number } | null>(null);
  const img = images[active];
  return (
    <div className="flex flex-col gap-3">
      <div
        className="relative aspect-square cursor-zoom-in overflow-hidden rounded-2xl bg-zinc-100"
        onMouseMove={(e) => {
          const r = e.currentTarget.getBoundingClientRect();
          setZoom({ x: ((e.clientX - r.left) / r.width) * 100, y: ((e.clientY - r.top) / r.height) * 100 });
        }}
        onMouseLeave={() => setZoom(null)}
        onClick={() => setZoom((z) => (z ? null : { x: 50, y: 50 }))}
      >
        {img && (
          <Image
            src={img.url}
            alt={img.alt || name}
            fill
            priority
            sizes="(max-width: 1024px) 100vw, 50vw"
            className="object-cover transition-transform duration-200"
            style={zoom ? { transform: "scale(2)", transformOrigin: `${zoom.x}% ${zoom.y}%` } : undefined}
          />
        )}
      </div>
      <div className="flex gap-2" role="tablist" aria-label="Ảnh sản phẩm">
        {images.map((im, i) => (
          <button
            key={im.url}
            role="tab"
            aria-selected={i === active}
            aria-label={`Ảnh ${i + 1}`}
            onClick={() => setActive(i)}
            className="relative size-16 overflow-hidden rounded-lg border-2 border-transparent aria-selected:border-brand-600"
          >
            <Image src={im.url} alt="" fill sizes="64px" className="object-cover" />
          </button>
        ))}
      </div>
    </div>
  );
}

export function AddToCart({ product, locale, t }: { product: ProductDetail; locale: Locale; t: Dictionary }) {
  const { addToCart } = useShop();
  const router = useRouter();
  const firstAvailable = product.variants.find((v) => v.available > 0) ?? product.variants[0];
  const [color, setColor] = useState(firstAvailable?.color ?? null);
  const [size, setSize] = useState(firstAvailable?.size ?? null);
  const [qty, setQty] = useState(1);
  const [busy, setBusy] = useState(false);
  const [msg, setMsg] = useState<{ ok: boolean; text: string } | null>(null);

  const variant = useMemo(
    () => product.variants.find((v) => v.color === color && v.size === size) ?? (product.variants.length === 1 ? product.variants[0] : undefined),
    [product.variants, color, size],
  );
  const availableFor = (c: string | null, s: string | null) => product.variants.find((v) => v.color === c && v.size === s)?.available ?? 0;

  async function add(goToCart: boolean) {
    if (!variant) return;
    setBusy(true);
    setMsg(null);
    try {
      await addToCart(variant.id, qty);
      setMsg({ ok: true, text: t.added });
      if (goToCart) router.push(`/${locale}/cart`);
    } catch (e) {
      setMsg({ ok: false, text: e instanceof ApiError ? e.message : t.error });
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="flex flex-col gap-5">
      {variant && <Price amount={variant.price} compareAt={variant.compareAtPrice} currency={product.currency} locale={locale} size="lg" />}

      {product.colors.length > 0 && (
        <fieldset>
          <legend className="mb-2 text-sm font-medium">
            {t.color}: <span className="font-normal">{color}</span>
          </legend>
          <div className="flex flex-wrap gap-2">
            {product.colors.map((c) => (
              <button
                key={c}
                type="button"
                aria-pressed={c === color}
                onClick={() => setColor(c)}
                className="min-h-11 rounded-lg border border-zinc-300 px-3 text-sm aria-pressed:border-brand-600 aria-pressed:bg-brand-50 aria-pressed:font-semibold"
              >
                {c}
              </button>
            ))}
          </div>
        </fieldset>
      )}

      {product.sizes.length > 0 && (
        <fieldset>
          <legend className="mb-2 text-sm font-medium">{t.size}</legend>
          <div className="flex flex-wrap gap-2">
            {product.sizes.map((s) => {
              const soldOut = availableFor(color, s) === 0;
              return (
                <button
                  key={s}
                  type="button"
                  aria-pressed={s === size}
                  onClick={() => setSize(s)}
                  className={`min-h-11 min-w-11 rounded-lg border border-zinc-300 px-3 text-sm aria-pressed:border-brand-600 aria-pressed:bg-brand-50 aria-pressed:font-semibold ${soldOut ? "text-zinc-400 line-through" : ""}`}
                >
                  {s}
                </button>
              );
            })}
          </div>
        </fieldset>
      )}

      <div className="flex items-center gap-3">
        <label htmlFor="qty" className="text-sm font-medium">
          {t.quantity}
        </label>
        <input
          id="qty"
          type="number"
          min={1}
          max={Math.max(1, Math.min(99, variant?.available ?? 1))}
          value={qty}
          onChange={(e) => setQty(Math.max(1, Number(e.target.value) || 1))}
          className="min-h-11 w-20 rounded-lg border border-zinc-300 px-2 text-center"
        />
        {variant &&
          (variant.available > 0 ? (
            <Badge tone={variant.available <= 5 ? "amber" : "green"}>
              {variant.available <= 5 ? `${t.left} ${variant.available}` : t.inStock}
            </Badge>
          ) : (
            <Badge tone="red">{t.outOfStock}</Badge>
          ))}
      </div>

      <div className="sticky bottom-0 -mx-4 flex gap-2 border-t border-zinc-200 bg-white p-4 sm:static sm:mx-0 sm:border-0 sm:p-0">
        <Button className="flex-1" variant="secondary" loading={busy} disabled={!variant || variant.available < qty} onClick={() => add(false)}>
          {t.addToCart}
        </Button>
        <Button className="flex-1" loading={busy} disabled={!variant || variant.available < qty} onClick={() => add(true)}>
          {t.buyNow}
        </Button>
      </div>
      <WishlistButton productId={product.id} t={t} />
      <p role="status" aria-live="polite" className={msg?.ok ? "text-green-700" : "text-red-600"}>
        {msg?.text}
      </p>
    </div>
  );
}

function WishlistButton({ productId, t }: { productId: string; t: Dictionary }) {
  const { user } = useShop();
  const [saved, setSaved] = useState(false);
  if (!user) return null;
  return (
    <Button
      variant="ghost"
      aria-pressed={saved}
      className="self-start"
      onClick={async () => {
        await api(`/api/v1/me/wishlist/${productId}`, { method: saved ? "DELETE" : "PUT" });
        setSaved(!saved);
      }}
    >
      {saved ? "♥" : "♡"} {t.wishlist}
    </Button>
  );
}

export function ReviewForm({ productId, t }: { productId: string; t: Dictionary }) {
  const { user } = useShop();
  const router = useRouter();
  const [rating, setRating] = useState(5);
  const [comment, setComment] = useState("");
  const [state, setState] = useState<{ busy?: boolean; error?: string; done?: Review }>({});
  if (!user) return <p className="text-sm text-zinc-500">{t.loginToReview}</p>;
  if (state.done) return <p className="text-green-700">✓ {state.done.comment}</p>;

  return (
    <form
      className="flex flex-col gap-3 rounded-xl border border-zinc-200 p-4"
      onSubmit={async (e) => {
        e.preventDefault();
        setState({ busy: true });
        try {
          const done = await api<Review>(`/api/v1/products/${productId}/reviews`, { method: "POST", json: { rating, comment } });
          setState({ done });
          router.refresh();
        } catch (err) {
          setState({ error: err instanceof ApiError ? err.message : t.error });
        }
      }}
    >
      <h3 className="font-semibold">{t.writeReview}</h3>
      <fieldset className="flex gap-1">
        <legend className="sr-only">Rating</legend>
        {[1, 2, 3, 4, 5].map((n) => (
          <button key={n} type="button" aria-label={`${n}/5`} aria-pressed={n <= rating} onClick={() => setRating(n)}
            className="text-2xl text-zinc-300 aria-pressed:text-amber-500">
            ★
          </button>
        ))}
      </fieldset>
      <label htmlFor="review-comment" className="sr-only">
        {t.writeReview}
      </label>
      <textarea id="review-comment" required maxLength={2000} rows={3} value={comment} onChange={(e) => setComment(e.target.value)}
        className="rounded-lg border border-zinc-300 p-3" />
      {state.error && <p className="text-sm text-red-600">{state.error}</p>}
      <Button type="submit" loading={state.busy} className="self-start">
        {t.submit}
      </Button>
    </form>
  );
}
