"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useCallback, useEffect, useState } from "react";
import { Badge, Button, Field, Spinner } from "@/components/atoms";
import { useShop } from "@/components/ShopProvider";
import { ApiError, api } from "@/lib/client-api";
import { formatMoney, type Dictionary } from "@/lib/i18n";
import type { Address, Locale, Order, OrderStatus, Paged } from "@/lib/types";

const statusTone: Record<OrderStatus, "zinc" | "blue" | "amber" | "green" | "red"> = {
  Created: "amber",
  Confirmed: "blue",
  Shipping: "blue",
  Completed: "green",
  Cancelled: "red",
};

export function StatusBadge({ status, t }: { status: OrderStatus; t: Dictionary }) {
  return <Badge tone={statusTone[status]}>{t.status[status]}</Badge>;
}

export function AccountView({ locale, t }: { locale: Locale; t: Dictionary }) {
  const { user, ready } = useShop();
  if (!ready) return <Spinner label={t.loading} />;
  return user ? <Dashboard locale={locale} t={t} /> : <AuthForms locale={locale} t={t} />;
}

function AuthForms({ locale, t }: { locale: Locale; t: Dictionary }) {
  const { login, register } = useShop();
  const router = useRouter();
  const [mode, setMode] = useState<"login" | "register">("login");
  const [form, setForm] = useState({ email: "", password: "", fullName: "" });
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  return (
    <div className="mx-auto flex max-w-sm flex-col gap-4 py-6">
      <h1 className="text-2xl font-bold">{mode === "login" ? t.login : t.register}</h1>
      <form
        className="flex flex-col gap-3 rounded-xl border border-zinc-200 bg-white p-4"
        onSubmit={async (e) => {
          e.preventDefault();
          setBusy(true);
          setError(null);
          try {
            if (mode === "login") await login(form.email, form.password);
            else await register(form.email, form.password, form.fullName);
            if (new URLSearchParams(window.location.search).get("next") === "checkout") router.push(`/${locale}/checkout`);
          } catch (err) {
            setError(err instanceof ApiError ? err.message : t.error);
          } finally {
            setBusy(false);
          }
        }}
      >
        {mode === "register" && (
          <Field id="fullName" label={t.fullName} required autoComplete="name" value={form.fullName}
            onChange={(e) => setForm({ ...form, fullName: e.target.value })} />
        )}
        <Field id="email" label={t.email} type="email" required autoComplete="email" value={form.email}
          onChange={(e) => setForm({ ...form, email: e.target.value })} />
        <Field id="password" label={t.password} type="password" required minLength={mode === "register" ? 8 : undefined}
          autoComplete={mode === "login" ? "current-password" : "new-password"} value={form.password}
          onChange={(e) => setForm({ ...form, password: e.target.value })} />
        {error && <p role="alert" className="text-sm text-red-600">{error}</p>}
        <Button type="submit" loading={busy}>
          {mode === "login" ? t.login : t.register}
        </Button>
      </form>
      <p className="text-sm">
        {mode === "login" ? t.noAccount : t.haveAccount}{" "}
        <button className="font-semibold text-brand-700" onClick={() => setMode(mode === "login" ? "register" : "login")}>
          {mode === "login" ? t.register : t.login}
        </button>
      </p>
    </div>
  );
}

type Tab = "orders" | "addresses" | "wishlist" | "notifications";

function Dashboard({ locale, t }: { locale: Locale; t: Dictionary }) {
  const { user, logout } = useShop();
  const [tab, setTab] = useState<Tab>("orders");
  const tabs: [Tab, string][] = [
    ["orders", t.myOrders],
    ["addresses", t.addressBook],
    ["wishlist", t.wishlist],
    ["notifications", t.notifications],
  ];
  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h1 className="text-2xl font-bold">{user!.fullName}</h1>
        <Button variant="ghost" onClick={logout}>
          {t.logout}
        </Button>
      </div>
      <div role="tablist" className="flex gap-2 overflow-x-auto">
        {tabs.map(([key, label]) => (
          <button key={key} role="tab" aria-selected={tab === key} onClick={() => setTab(key)}
            className="min-h-11 whitespace-nowrap rounded-full border border-zinc-300 px-4 aria-selected:border-brand-600 aria-selected:bg-brand-50 aria-selected:font-semibold">
            {label}
          </button>
        ))}
      </div>
      <div role="tabpanel">
        {tab === "orders" && <Orders locale={locale} t={t} />}
        {tab === "addresses" && <Addresses t={t} />}
        {tab === "wishlist" && <Wishlist locale={locale} t={t} />}
        {tab === "notifications" && <Notifications t={t} />}
      </div>
    </div>
  );
}

function useLoad<T>(path: string) {
  const [data, setData] = useState<T | null>(null);
  const reload = useCallback(() => api<T>(path).then(setData).catch(() => setData(null)), [path]);
  useEffect(() => {
    void reload();
  }, [reload]);
  return [data, reload] as const;
}

function Orders({ locale, t }: { locale: Locale; t: Dictionary }) {
  const [data, reload] = useLoad<Paged<Order>>("/api/v1/orders");
  if (!data) return <Spinner label={t.loading} />;
  if (!data.items.length) return <p className="text-zinc-500">{t.noOrders}</p>;
  return (
    <ul className="flex flex-col gap-3">
      {data.items.map((o) => (
        <li key={o.id} className="rounded-xl border border-zinc-200 bg-white p-4">
          <div className="flex flex-wrap items-center gap-2">
            <strong>{o.number}</strong>
            <StatusBadge status={o.status} t={t} />
            <span className="ml-auto font-semibold">{formatMoney(o.total, o.currency, locale)}</span>
          </div>
          <p className="mt-1 text-sm text-zinc-500">
            {new Date(o.createdAt).toLocaleString(locale === "vi" ? "vi-VN" : "en-US")} · {o.items.length} SP
            {o.trackingNumber && ` · ${o.trackingNumber}`}
          </p>
          <ul className="mt-2 text-sm text-zinc-700">
            {o.items.map((i) => (
              <li key={i.sku}>
                {i.productName} ({i.variantName}) × {i.quantity}
              </li>
            ))}
          </ul>
          {o.status === "Created" && (
            <Button variant="secondary" className="mt-3"
              onClick={async () => {
                await api(`/api/v1/orders/${o.id}/cancel`, { method: "POST" }).catch(() => {});
                void reload();
              }}>
              {t.cancelOrder}
            </Button>
          )}
        </li>
      ))}
    </ul>
  );
}

function Addresses({ t }: { t: Dictionary }) {
  const [list, reload] = useLoad<Address[]>("/api/v1/me/addresses");
  const [form, setForm] = useState<Address>({ fullName: "", phone: "", line1: "", city: "", district: "", ward: "" });
  const [error, setError] = useState<string | null>(null);
  const set = (k: keyof Address) => (e: React.ChangeEvent<HTMLInputElement>) => setForm({ ...form, [k]: e.target.value });
  return (
    <div className="grid gap-4 lg:grid-cols-2">
      <ul className="flex flex-col gap-2">
        {list?.map((a) => (
          <li key={a.id} className="rounded-xl border border-zinc-200 bg-white p-3 text-sm">
            <strong>{a.fullName}</strong> · {a.phone} {a.isDefault && <Badge tone="green">Default</Badge>}
            <br />
            {[a.line1, a.ward, a.district, a.city].filter(Boolean).join(", ")}
            <button className="ml-2 text-red-600 underline"
              onClick={() => api(`/api/v1/me/addresses/${a.id}`, { method: "DELETE" }).then(reload)}>
              {t.remove}
            </button>
          </li>
        ))}
      </ul>
      <form className="grid gap-3 rounded-xl border border-zinc-200 bg-white p-4"
        onSubmit={async (e) => {
          e.preventDefault();
          setError(null);
          try {
            await api("/api/v1/me/addresses", { method: "POST", json: { ...form, isDefault: !list?.length } });
            setForm({ fullName: "", phone: "", line1: "", city: "", district: "", ward: "" });
            void reload();
          } catch (err) {
            setError(err instanceof ApiError ? err.message : t.error);
          }
        }}>
        <Field id="a-name" label={t.fullName} required value={form.fullName} onChange={set("fullName")} />
        <Field id="a-phone" label={t.phone} type="tel" required value={form.phone} onChange={set("phone")} />
        <Field id="a-line1" label={t.address} required value={form.line1} onChange={set("line1")} />
        <Field id="a-district" label={t.district} value={form.district ?? ""} onChange={set("district")} />
        <Field id="a-city" label={t.city} required value={form.city} onChange={set("city")} />
        {error && <p className="text-sm text-red-600">{error}</p>}
        <Button type="submit">+ {t.addressBook}</Button>
      </form>
    </div>
  );
}

function Wishlist({ locale, t }: { locale: Locale; t: Dictionary }) {
  const [items] = useLoad<{ productId: string; slug: string; name: string; minPrice: number }[]>("/api/v1/me/wishlist");
  if (!items) return <Spinner label={t.loading} />;
  if (!items.length) return <p className="text-zinc-500">—</p>;
  return (
    <ul className="grid gap-2 sm:grid-cols-2">
      {items.map((w) => (
        <li key={w.productId} className="rounded-xl border border-zinc-200 bg-white p-3">
          <Link href={`/${locale}/products/${w.slug}`} className="font-medium">
            {w.name}
          </Link>
          <p className="text-sm text-brand-700">{formatMoney(w.minPrice, "VND", locale)}</p>
        </li>
      ))}
    </ul>
  );
}

function Notifications({ t }: { t: Dictionary }) {
  const [items] = useLoad<{ id: string; subject: string; isRead: boolean; createdAt: string }[]>("/api/v1/me/notifications");
  useEffect(() => {
    void api("/api/v1/me/notifications/read-all", { method: "POST" }).catch(() => {});
  }, []);
  if (!items) return <Spinner label={t.loading} />;
  return (
    <ul className="flex flex-col gap-2">
      {items.map((n) => (
        <li key={n.id} className={`rounded-xl border bg-white p-3 text-sm ${n.isRead ? "border-zinc-200" : "border-brand-600"}`}>
          {n.subject}
          <time className="block text-xs text-zinc-400">{new Date(n.createdAt).toLocaleString()}</time>
        </li>
      ))}
    </ul>
  );
}
