"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { Button, Field, Spinner } from "@/components/atoms";
import { useShop } from "@/components/ShopProvider";
import { ApiError, api } from "@/lib/client-api";
import { formatMoney, type Dictionary } from "@/lib/i18n";
import type { Address, Locale, Order, Quote } from "@/lib/types";

type Step = 0 | 1 | 2;
interface CheckoutResult {
  order: Order;
  paymentId: string;
  paymentUrl: string | null;
}

const emptyAddress: Address = { fullName: "", phone: "", line1: "", ward: "", district: "", city: "" };

/** Checkout 3 bước: Thông tin giao hàng → Phương thức thanh toán → Xác nhận. Hỗ trợ guest & thành viên. */
export function CheckoutFlow({ locale, t }: { locale: Locale; t: Dictionary }) {
  const { cart, user, currency, ready, resetCart } = useShop();
  const router = useRouter();
  const [step, setStep] = useState<Step>(0);
  const [email, setEmail] = useState("");
  const [address, setAddress] = useState<Address>(emptyAddress);
  const [method, setMethod] = useState<"Cod" | "MockPay">("MockPay");
  const [promo, setPromo] = useState("");
  const [appliedPromo, setAppliedPromo] = useState<string | null>(null);
  const [quote, setQuote] = useState<Quote | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  // Thành viên: điền sẵn email + địa chỉ mặc định từ sổ địa chỉ.
  useEffect(() => {
    if (!user) return;
    let cancelled = false;
    api<Address[]>("/api/v1/me/addresses")
      .catch(() => [] as Address[])
      .then((list) => {
        if (cancelled) return;
        setEmail((e) => e || user.email);
        const def = list.find((a) => a.isDefault) ?? list[0];
        setAddress((a) => (a.fullName ? a : (def ?? { ...a, fullName: user.fullName })));
      });
    return () => {
      cancelled = true;
    };
  }, [user]);

  useEffect(() => {
    if (!cart?.items.length) return;
    api<Quote>("/api/v1/checkout/quote", { method: "POST", json: { cartId: cart.id, promotionCode: appliedPromo, currency } })
      .then(setQuote)
      .catch((e) => {
        if (appliedPromo) setAppliedPromo(null);
        setError(e instanceof ApiError ? e.message : t.error);
      });
  }, [cart, appliedPromo, currency, t.error]);

  if (!ready) return <Spinner label={t.loading} />;
  if (!cart?.items.length)
    return (
      <p className="py-16 text-center">
        {t.emptyCart}{" "}
        <Link className="font-semibold text-brand-700" href={`/${locale}/products`}>
          {t.continueShopping}
        </Link>
      </p>
    );

  const set = (k: keyof Address) => (e: React.ChangeEvent<HTMLInputElement>) => setAddress((a) => ({ ...a, [k]: e.target.value }));
  const steps = [t.stepInfo, t.stepPayment, t.stepReview];

  async function placeOrder() {
    setBusy(true);
    setError(null);
    try {
      const res = await api<CheckoutResult>("/api/v1/checkout", {
        method: "POST",
        json: { cartId: cart!.id, email, shippingAddress: address, paymentMethod: method, promotionCode: appliedPromo, currency },
      });
      resetCart();
      if (res.paymentUrl) router.push(`/${locale}/checkout/pay/${res.paymentId}`);
      else router.push(`/${locale}/checkout/success?number=${res.order.number}&email=${encodeURIComponent(email)}`);
    } catch (e) {
      setError(e instanceof ApiError ? e.message : t.error);
      setBusy(false);
    }
  }

  return (
    <div className="grid gap-6 lg:grid-cols-[1fr_340px]">
      <section className="flex flex-col gap-5">
        <h1 className="text-2xl font-bold">{t.checkout}</h1>
        <ol className="flex gap-2 text-sm" aria-label="Checkout steps">
          {steps.map((s, i) => (
            <li key={s} aria-current={i === step ? "step" : undefined}
              className={`flex-1 rounded-full px-3 py-2 text-center font-medium ${i <= step ? "bg-brand-600 text-white" : "bg-zinc-200 text-zinc-600"}`}>
              {i + 1}. {s}
            </li>
          ))}
        </ol>
        {!user && step === 0 && (
          <p className="text-sm text-zinc-600">
            {t.guestCheckout}{" "}
            <Link href={`/${locale}/account?next=checkout`} className="font-semibold text-brand-700">
              {t.login}
            </Link>
          </p>
        )}
        {error && (
          <p role="alert" className="rounded-lg bg-red-50 p-3 text-red-700">
            {error}
          </p>
        )}

        {step === 0 && (
          <form
            className="grid gap-4 rounded-xl border border-zinc-200 bg-white p-4 sm:grid-cols-2"
            onSubmit={(e) => {
              e.preventDefault();
              setError(null);
              setStep(1);
            }}
          >
            <div className="sm:col-span-2">
              <Field id="email" label={t.email} type="email" autoComplete="email" required value={email} onChange={(e) => setEmail(e.target.value)} />
            </div>
            <Field id="fullName" label={t.fullName} autoComplete="name" required value={address.fullName} onChange={set("fullName")} />
            <Field id="phone" label={t.phone} type="tel" autoComplete="tel" inputMode="tel" required pattern="^\+?[0-9 ]{8,15}$"
              value={address.phone} onChange={set("phone")} />
            <div className="sm:col-span-2">
              <Field id="line1" label={t.address} autoComplete="street-address" required value={address.line1} onChange={set("line1")} />
            </div>
            <Field id="ward" label={t.ward} value={address.ward ?? ""} onChange={set("ward")} />
            <Field id="district" label={t.district} value={address.district ?? ""} onChange={set("district")} />
            <Field id="city" label={t.city} autoComplete="address-level1" required value={address.city} onChange={set("city")} />
            <div className="flex items-end sm:col-span-2">
              <Button type="submit" className="w-full sm:w-auto">
                {t.next}
              </Button>
            </div>
          </form>
        )}

        {step === 1 && (
          <div className="flex flex-col gap-4 rounded-xl border border-zinc-200 bg-white p-4">
            <fieldset className="flex flex-col gap-2">
              <legend className="mb-2 font-semibold">{t.stepPayment}</legend>
              {(["MockPay", "Cod"] as const).map((m) => (
                <label key={m} className="flex min-h-11 cursor-pointer items-center gap-3 rounded-lg border border-zinc-300 p-3 has-checked:border-brand-600 has-checked:bg-brand-50">
                  <input type="radio" name="method" value={m} checked={method === m} onChange={() => setMethod(m)} className="size-4 accent-brand-600" />
                  {m === "Cod" ? t.cod : t.mockpay}
                </label>
              ))}
            </fieldset>
            <div className="flex gap-2">
              <Button variant="secondary" onClick={() => setStep(0)}>
                {t.back}
              </Button>
              <Button onClick={() => setStep(2)}>{t.next}</Button>
            </div>
          </div>
        )}

        {step === 2 && (
          <div className="flex flex-col gap-4 rounded-xl border border-zinc-200 bg-white p-4">
            <dl className="grid gap-2 text-sm sm:grid-cols-[140px_1fr]">
              <dt className="text-zinc-500">{t.email}</dt>
              <dd>{email}</dd>
              <dt className="text-zinc-500">{t.address}</dt>
              <dd>
                {address.fullName} · {address.phone}
                <br />
                {[address.line1, address.ward, address.district, address.city].filter(Boolean).join(", ")}
              </dd>
              <dt className="text-zinc-500">{t.stepPayment}</dt>
              <dd>{method === "Cod" ? t.cod : t.mockpay}</dd>
            </dl>
            <ul className="divide-y divide-zinc-100 text-sm">
              {cart.items.map((l) => (
                <li key={l.id} className="flex justify-between py-2">
                  <span>
                    {l.productName} <span className="text-zinc-500">({l.variantName}) × {l.quantity}</span>
                  </span>
                  <span>{formatMoney(l.lineTotal, cart.currency, locale)}</span>
                </li>
              ))}
            </ul>
            <div className="flex gap-2">
              <Button variant="secondary" onClick={() => setStep(1)}>
                {t.back}
              </Button>
              <Button loading={busy} onClick={placeOrder} className="flex-1">
                {t.placeOrder}
              </Button>
            </div>
          </div>
        )}
      </section>

      <aside className="flex h-fit flex-col gap-3 rounded-xl border border-zinc-200 bg-white p-4 lg:sticky lg:top-24">
        <form
          className="flex gap-2"
          onSubmit={(e) => {
            e.preventDefault();
            setError(null);
            setAppliedPromo(promo.trim().toUpperCase() || null);
          }}
        >
          <label htmlFor="promo" className="sr-only">
            {t.promoCode}
          </label>
          <input id="promo" value={promo} onChange={(e) => setPromo(e.target.value)} placeholder={t.promoCode}
            className="min-h-11 flex-1 rounded-lg border border-zinc-300 px-3 uppercase" />
          <Button variant="secondary" type="submit">
            {t.apply}
          </Button>
        </form>
        {quote && (
          <dl className="flex flex-col gap-2 text-sm">
            <div className="flex justify-between">
              <dt>{t.subtotal}</dt>
              <dd>{formatMoney(quote.subtotal, quote.currency, locale)}</dd>
            </div>
            {quote.discount > 0 && (
              <div className="flex justify-between text-green-700">
                <dt>
                  {t.discount} ({quote.appliedPromotion})
                </dt>
                <dd>−{formatMoney(quote.discount, quote.currency, locale)}</dd>
              </div>
            )}
            <div className="flex justify-between">
              <dt>{t.shipping}</dt>
              <dd>{quote.shippingFee === 0 ? t.free : formatMoney(quote.shippingFee, quote.currency, locale)}</dd>
            </div>
            <div className="flex justify-between border-t border-zinc-200 pt-2 text-base font-bold">
              <dt>{t.total}</dt>
              <dd>{formatMoney(quote.total, quote.currency, locale)}</dd>
            </div>
          </dl>
        )}
      </aside>
    </div>
  );
}
