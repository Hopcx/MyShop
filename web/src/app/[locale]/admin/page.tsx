import type { Metadata } from "next";
import { AdminView } from "./AdminView";
import { getDictionary, isLocale } from "@/lib/i18n";

export const metadata: Metadata = { title: "Admin", robots: { index: false, follow: false } };

export default async function AdminPage({ params }: PageProps<"/[locale]/admin">) {
  const { locale } = await params;
  const l = isLocale(locale) ? locale : "vi";
  return <AdminView locale={l} t={getDictionary(l)} />;
}
