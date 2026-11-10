import type { Metadata } from "next";
import { CheckoutFlow } from "./CheckoutFlow";
import { getDictionary, isLocale } from "@/lib/i18n";

export const metadata: Metadata = { robots: { index: false } };

export default async function CheckoutPage({ params }: PageProps<"/[locale]/checkout">) {
  const { locale } = await params;
  const l = isLocale(locale) ? locale : "vi";
  return <CheckoutFlow locale={l} t={getDictionary(l)} />;
}
