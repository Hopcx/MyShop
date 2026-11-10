import type { Metadata, Viewport } from "next";
import { Be_Vietnam_Pro } from "next/font/google";
import { notFound } from "next/navigation";
import { ShopProvider } from "@/components/ShopProvider";
import { Header } from "@/components/organisms/Header";
import { getDictionary, isLocale, locales } from "@/lib/i18n";
import "../globals.css";

const font = Be_Vietnam_Pro({ subsets: ["latin", "vietnamese"], weight: ["400", "500", "600", "700", "800"], variable: "--font-vn" });

const SITE_URL = process.env.NEXT_PUBLIC_SITE_URL ?? "http://localhost:3000";

export function generateStaticParams() {
  return locales.map((locale) => ({ locale }));
}

export async function generateMetadata({ params }: LayoutProps<"/[locale]">): Promise<Metadata> {
  const { locale } = await params;
  const t = getDictionary(isLocale(locale) ? locale : "vi");
  return {
    metadataBase: new URL(SITE_URL),
    title: { default: `${t.brand} — ${t.tagline}`, template: `%s | ${t.brand}` },
    description: t.tagline,
    alternates: { languages: { vi: "/vi", en: "/en" } },
    openGraph: { siteName: t.brand, locale: locale === "vi" ? "vi_VN" : "en_US", type: "website" },
  };
}

export const viewport: Viewport = { width: "device-width", initialScale: 1, themeColor: "#0f766e" };

export default async function LocaleLayout({ children, params }: LayoutProps<"/[locale]">) {
  const { locale } = await params;
  if (!isLocale(locale)) notFound();
  const t = getDictionary(locale);

  return (
    <html lang={locale} className={`${font.variable} h-full antialiased`}>
      <body className="flex min-h-full flex-col bg-zinc-50 text-zinc-900">
        <ShopProvider>
          <Header locale={locale} t={t} />
          <main id="main" className="mx-auto w-full max-w-7xl flex-1 px-4 py-6">
            {children}
          </main>
          <footer className="border-t border-zinc-200 bg-white">
            <div className="mx-auto flex max-w-7xl flex-col gap-2 px-4 py-6 text-sm text-zinc-500 sm:flex-row sm:justify-between">
              <p>© {t.brand}</p>
              <p>{t.freeShipNote}</p>
            </div>
          </footer>
        </ShopProvider>
      </body>
    </html>
  );
}
