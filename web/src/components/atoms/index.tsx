import type { ButtonHTMLAttributes, InputHTMLAttributes, ReactNode } from "react";
import { formatMoney } from "@/lib/i18n";
import type { Locale } from "@/lib/types";

/* Atoms: thành phần UI nhỏ nhất, không phụ thuộc dữ liệu nghiệp vụ. */

type Variant = "primary" | "secondary" | "ghost" | "danger";
const variants: Record<Variant, string> = {
  primary: "bg-brand-600 text-white hover:bg-brand-700 disabled:bg-zinc-300",
  secondary: "border border-zinc-300 bg-white text-zinc-900 hover:bg-zinc-50 disabled:text-zinc-400",
  ghost: "text-zinc-700 hover:bg-zinc-100",
  danger: "bg-red-600 text-white hover:bg-red-700",
};

export function Button({
  variant = "primary",
  className = "",
  loading,
  children,
  ...props
}: ButtonHTMLAttributes<HTMLButtonElement> & { variant?: Variant; loading?: boolean }) {
  return (
    <button
      className={`inline-flex min-h-11 items-center justify-center gap-2 rounded-lg px-4 py-2 text-sm font-semibold transition focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-600 disabled:cursor-not-allowed ${variants[variant]} ${className}`}
      disabled={loading || props.disabled}
      aria-busy={loading || undefined}
      {...props}
    >
      {loading && <span className="size-4 animate-spin rounded-full border-2 border-current border-t-transparent" aria-hidden />}
      {children}
    </button>
  );
}

export function Field({
  label,
  error,
  id,
  ...props
}: InputHTMLAttributes<HTMLInputElement> & { label: string; error?: string; id: string }) {
  return (
    <div className="flex flex-col gap-1">
      <label htmlFor={id} className="text-sm font-medium text-zinc-700">
        {label}
      </label>
      <input
        id={id}
        aria-invalid={!!error || undefined}
        aria-describedby={error ? `${id}-err` : undefined}
        className="min-h-11 rounded-lg border border-zinc-300 px-3 text-base outline-none focus:border-brand-600 focus:ring-2 focus:ring-brand-600/20 aria-invalid:border-red-500"
        {...props}
      />
      {error && (
        <p id={`${id}-err`} className="text-sm text-red-600">
          {error}
        </p>
      )}
    </div>
  );
}

export function Price({
  amount,
  compareAt,
  currency,
  locale,
  size = "md",
}: {
  amount: number;
  compareAt?: number | null;
  currency: string;
  locale: Locale;
  size?: "md" | "lg";
}) {
  const discount = compareAt && compareAt > amount ? Math.round((1 - amount / compareAt) * 100) : 0;
  return (
    <span className="flex flex-wrap items-baseline gap-2">
      <span className={`font-bold text-brand-700 ${size === "lg" ? "text-2xl" : "text-base"}`}>{formatMoney(amount, currency, locale)}</span>
      {discount > 0 && (
        <>
          <s className="text-sm text-zinc-500">{formatMoney(compareAt!, currency, locale)}</s>
          <Badge tone="red">-{discount}%</Badge>
        </>
      )}
    </span>
  );
}

export function Badge({ children, tone = "zinc" }: { children: ReactNode; tone?: "zinc" | "red" | "green" | "amber" | "blue" }) {
  const tones = {
    zinc: "bg-zinc-100 text-zinc-700",
    red: "bg-red-50 text-red-700",
    green: "bg-green-50 text-green-700",
    amber: "bg-amber-50 text-amber-800",
    blue: "bg-blue-50 text-blue-700",
  };
  return <span className={`inline-flex items-center rounded-full px-2 py-0.5 text-xs font-semibold ${tones[tone]}`}>{children}</span>;
}

export function Rating({ value, count }: { value: number; count?: number }) {
  return (
    <span className="inline-flex items-center gap-1 text-sm text-zinc-600" aria-label={`${value} / 5`}>
      <span aria-hidden className="text-amber-500">
        {"★★★★★".slice(0, Math.round(value))}
        <span className="text-zinc-300">{"★★★★★".slice(Math.round(value))}</span>
      </span>
      {value.toFixed(1)}
      {count !== undefined && <span className="text-zinc-400">({count})</span>}
    </span>
  );
}

export function Spinner({ label }: { label: string }) {
  return (
    <div role="status" className="flex items-center justify-center gap-2 py-10 text-zinc-500">
      <span className="size-5 animate-spin rounded-full border-2 border-zinc-400 border-t-transparent" aria-hidden />
      <span>{label}</span>
    </div>
  );
}

export function Skeleton({ className = "" }: { className?: string }) {
  return <div className={`animate-pulse rounded-lg bg-zinc-200 ${className}`} aria-hidden />;
}
