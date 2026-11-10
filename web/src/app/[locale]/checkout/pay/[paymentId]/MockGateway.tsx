"use client";

import { useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { Button, Spinner } from "@/components/atoms";
import { ApiError, api } from "@/lib/client-api";
import { formatMoney, type Dictionary } from "@/lib/i18n";
import type { Locale } from "@/lib/types";

interface PaymentInfo {
  id: string;
  status: string;
  orderNumber: string;
  amount: number;
  currency: string;
}

/**
 * Trang mô phỏng cổng thanh toán (thay cho redirect sang VNPay/MoMo/Stripe).
 * Không thu thập thông tin thẻ — theo PCI-DSS SAQ A, dữ liệu thẻ chỉ được nhập trên trang của cổng thanh toán.
 */
export function MockGateway({ paymentId, locale, t }: { paymentId: string; locale: Locale; t: Dictionary }) {
  const router = useRouter();
  const [info, setInfo] = useState<PaymentInfo | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api<PaymentInfo>(`/api/v1/payments/${paymentId}`).then(setInfo).catch(() => setError(t.error));
  }, [paymentId, t.error]);

  async function simulate(success: boolean) {
    setBusy(true);
    try {
      await api(`/api/v1/payments/mockpay/${paymentId}/simulate?success=${success}`, { method: "POST" });
      router.push(`/${locale}/checkout/success?number=${info!.orderNumber}&paid=${success}`);
    } catch (e) {
      setError(e instanceof ApiError ? e.message : t.error);
      setBusy(false);
    }
  }

  if (error) return <p role="alert" className="py-16 text-center text-red-600">{error}</p>;
  if (!info) return <Spinner label={t.loading} />;

  return (
    <div className="mx-auto flex max-w-md flex-col gap-4 rounded-2xl border border-zinc-200 bg-white p-6 text-center">
      <p className="text-sm font-semibold uppercase tracking-wide text-zinc-500">MockPay · Sandbox</p>
      <p>
        {t.orderNumber}: <strong>{info.orderNumber}</strong>
      </p>
      <p className="text-3xl font-extrabold text-brand-700">{formatMoney(info.amount, info.currency, locale)}</p>
      {info.status !== "Pending" ? (
        <p className="text-zinc-600">{info.status}</p>
      ) : (
        <div className="flex flex-col gap-2">
          <Button loading={busy} onClick={() => simulate(true)}>
            {t.paySuccess}
          </Button>
          <Button variant="secondary" disabled={busy} onClick={() => simulate(false)}>
            {t.payFail}
          </Button>
        </div>
      )}
    </div>
  );
}
