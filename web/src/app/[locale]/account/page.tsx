import type { Metadata } from "next";
import { AccountView } from "./AccountView";
import { getDictionary, isLocale } from "@/lib/i18n";

export const metadata: Metadata = { robots: { index: false } };

export default async function AccountPage({ params }: PageProps<"/[locale]/account">) {
  const { locale } = await params;
  const l = isLocale(locale) ? locale : "vi";
  return <AccountView locale={l} t={getDictionary(l)} />;
}
