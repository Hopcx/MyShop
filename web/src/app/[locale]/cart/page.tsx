import type { Metadata } from "next";
import { CartView } from "./CartView";
import { getDictionary, isLocale } from "@/lib/i18n";

export const metadata: Metadata = { robots: { index: false } };

export default async function CartPage({ params }: PageProps<"/[locale]/cart">) {
  const { locale } = await params;
  const l = isLocale(locale) ? locale : "vi";
  return <CartView locale={l} t={getDictionary(l)} />;
}
