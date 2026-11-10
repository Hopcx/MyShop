import type { Metadata } from "next";
import Link from "next/link";
import { Suspense } from "react";
import { getDictionary, isLocale } from "@/lib/i18n";

export const metadata: Metadata = { robots: { index: false } };

export default function SuccessPage({ params, searchParams }: PageProps<"/[locale]/checkout/success">) {
  return (
    <Suspense>
      <Content params={params} searchParams={searchParams} />
    </Suspense>
  );
}

async function Content({ params, searchParams }: Pick<PageProps<"/[locale]/checkout/success">, "params" | "searchParams">) {
  const [{ locale }, sp] = await Promise.all([params, searchParams]);
  const l = isLocale(locale) ? locale : "vi";
  const t = getDictionary(l);
  const number = typeof sp.number === "string" ? sp.number.slice(0, 32) : "";
  const failed = sp.paid === "false";
  return (
    <div className="mx-auto flex max-w-md flex-col items-center gap-4 py-12 text-center">
      <div className={`flex size-16 items-center justify-center rounded-full text-3xl ${failed ? "bg-red-50 text-red-600" : "bg-green-50 text-green-600"}`} aria-hidden>
        {failed ? "✕" : "✓"}
      </div>
      <h1 className="text-2xl font-bold">{failed ? t.error : t.orderPlaced}</h1>
      <p>
        {t.orderNumber}: <strong>{number}</strong>
      </p>
      <div className="flex gap-3">
        <Link href={`/${l}/account`} className="font-semibold text-brand-700">
          {t.myOrders}
        </Link>
        <Link href={`/${l}/products`} className="font-semibold text-brand-700">
          {t.continueShopping}
        </Link>
      </div>
    </div>
  );
}
