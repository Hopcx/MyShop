"use client";

import { useCallback, useEffect, useState } from "react";
import { Button, Spinner } from "@/components/atoms";
import { useShop } from "@/components/ShopProvider";
import { ApiError, api } from "@/lib/client-api";
import { formatMoney, type Dictionary } from "@/lib/i18n";
import type { Locale, Order, OrderStatus, Paged } from "@/lib/types";
import { StatusBadge } from "../account/AccountView";

interface Summary {
  orders: number;
  cancelledOrders: number;
  revenue: number;
  averageOrderValue: number;
  conversionRate: number;
  averageCustomerLifetimeValue: number;
  daily: { date: string; orders: number; revenue: number }[];
  topProducts: { productId: string; name: string; quantity: number; revenue: number }[];
}

const actions: Partial<Record<OrderStatus, { label: string; path: string; body?: object }[]>> = {
  Created: [
    { label: "Xác nhận", path: "confirm", body: {} },
    { label: "Hủy", path: "cancel", body: {} },
  ],
  Confirmed: [
    { label: "Giao hàng (GHN)", path: "ship", body: { carrier: "GHN" } },
    { label: "Hủy & hoàn tiền", path: "cancel", body: {} },
  ],
  Shipping: [{ label: "Hoàn thành", path: "complete", body: {} }],
};

/** Back-office tối giản: KPI dashboard + xử lý đơn theo quy trình 4 bước. */
export function AdminView({ locale, t }: { locale: Locale; t: Dictionary }) {
  const { user, ready } = useShop();
  const [summary, setSummary] = useState<Summary | null>(null);
  const [orders, setOrders] = useState<Paged<Order> | null>(null);
  const [filter, setFilter] = useState<OrderStatus | "">("");
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    try {
      const [s, o] = await Promise.all([
        api<Summary>("/api/v1/admin/reports/summary"),
        api<Paged<Order>>(`/api/v1/admin/orders?pageSize=50${filter ? `&status=${filter}` : ""}`),
      ]);
      setSummary(s);
      setOrders(o);
    } catch (e) {
      setError(e instanceof ApiError ? e.message : t.error);
    }
  }, [filter, t.error]);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- tải dữ liệu khi quyền admin sẵn sàng
    if (user?.role === "Admin") void load();
  }, [user, load]);

  if (!ready) return <Spinner label={t.loading} />;
  if (user?.role !== "Admin") return <p className="py-16 text-center text-zinc-500">403</p>;

  const max = Math.max(1, ...(summary?.daily.map((d) => d.revenue) ?? [1]));

  return (
    <div className="flex flex-col gap-6">
      <h1 className="text-2xl font-bold">{t.admin}</h1>
      {error && <p role="alert" className="rounded-lg bg-red-50 p-3 text-red-700">{error}</p>}
      {summary && (
        <section aria-label="KPI" className="grid grid-cols-2 gap-3 lg:grid-cols-5">
          {[
            ["Doanh thu 30 ngày", formatMoney(summary.revenue, "VND", locale)],
            ["Số đơn", String(summary.orders)],
            ["AOV", formatMoney(summary.averageOrderValue, "VND", locale)],
            ["CR (đơn/giỏ)", `${(summary.conversionRate * 100).toFixed(1)}%`],
            ["LTV TB", formatMoney(summary.averageCustomerLifetimeValue, "VND", locale)],
          ].map(([label, value]) => (
            <div key={label} className="rounded-xl border border-zinc-200 bg-white p-4">
              <p className="text-xs text-zinc-500">{label}</p>
              <p className="text-lg font-bold">{value}</p>
            </div>
          ))}
        </section>
      )}
      {summary && summary.daily.length > 0 && (
        <section className="rounded-xl border border-zinc-200 bg-white p-4">
          <h2 className="mb-3 font-semibold">Doanh thu theo ngày</h2>
          <div className="flex h-32 items-end gap-1" role="img" aria-label="Biểu đồ doanh thu theo ngày">
            {summary.daily.map((d) => (
              <div key={d.date} title={`${d.date}: ${formatMoney(d.revenue, "VND", locale)}`}
                className="flex-1 rounded-t bg-brand-600" style={{ height: `${(d.revenue / max) * 100}%` }} />
            ))}
          </div>
        </section>
      )}
      <section className="flex flex-col gap-3">
        <div className="flex flex-wrap items-center gap-2">
          <h2 className="font-semibold">Đơn hàng</h2>
          <select value={filter} onChange={(e) => setFilter(e.target.value as OrderStatus | "")}
            className="min-h-11 rounded-lg border border-zinc-300 px-2" aria-label="Lọc trạng thái">
            <option value="">Tất cả</option>
            {(Object.keys(t.status) as OrderStatus[]).map((s) => (
              <option key={s} value={s}>{t.status[s]}</option>
            ))}
          </select>
        </div>
        <div className="overflow-x-auto rounded-xl border border-zinc-200 bg-white">
          <table className="w-full text-sm">
            <thead className="bg-zinc-50 text-left">
              <tr>
                <th className="p-3">Mã</th>
                <th className="p-3">Email</th>
                <th className="p-3">Tổng</th>
                <th className="p-3">TT</th>
                <th className="p-3">Trạng thái</th>
                <th className="p-3">Thao tác</th>
              </tr>
            </thead>
            <tbody>
              {orders?.items.map((o) => (
                <tr key={o.id} className="border-t border-zinc-100">
                  <td className="p-3 font-mono">{o.number}</td>
                  <td className="p-3">{o.email}</td>
                  <td className="p-3">{formatMoney(o.total, o.currency, locale)}</td>
                  <td className="p-3">{o.paymentMethod}</td>
                  <td className="p-3"><StatusBadge status={o.status} t={t} /></td>
                  <td className="flex flex-wrap gap-1 p-3">
                    {actions[o.status]?.map((a) => (
                      <Button key={a.path} variant={a.path === "cancel" ? "secondary" : "primary"} className="min-h-9 px-2 py-1 text-xs"
                        onClick={async () => {
                          try {
                            await api(`/api/v1/admin/orders/${o.id}/${a.path}`, { method: "POST", json: a.body });
                            await load();
                          } catch (e) {
                            setError(e instanceof ApiError ? e.message : t.error);
                          }
                        }}>
                        {a.label}
                      </Button>
                    ))}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>
    </div>
  );
}
