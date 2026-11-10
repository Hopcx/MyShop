import type { Metadata } from "next";
import { Suspense } from "react";
import { MockGateway } from "./MockGateway";
import { getDictionary, isLocale } from "@/lib/i18n";

export const metadata: Metadata = { robots: { index: false } };

type Props = PageProps<"/[locale]/checkout/pay/[paymentId]">;

export default function PayPage({ params }: Props) {
  return (
    <Suspense>
      <Content params={params} />
    </Suspense>
  );
}

async function Content({ params }: Pick<Props, "params">) {
  const { locale, paymentId } = await params;
  const l = isLocale(locale) ? locale : "vi";
  return <MockGateway paymentId={paymentId} locale={l} t={getDictionary(l)} />;
}
