import { NextResponse, type NextRequest } from "next/server";

const locales = ["vi", "en"];

/** Chuyển hướng URL thiếu tiền tố ngôn ngữ (/products → /vi/products) theo cookie hoặc Accept-Language. */
export function proxy(request: NextRequest) {
  const { pathname } = request.nextUrl;
  if (locales.some((l) => pathname === `/${l}` || pathname.startsWith(`/${l}/`))) return;

  const cookie = request.cookies.get("locale")?.value;
  const accept = request.headers.get("accept-language")?.toLowerCase() ?? "";
  const locale = cookie && locales.includes(cookie) ? cookie : accept.startsWith("en") ? "en" : "vi";
  request.nextUrl.pathname = `/${locale}${pathname === "/" ? "" : pathname}`;
  return NextResponse.redirect(request.nextUrl);
}

export const config = {
  matcher: ["/((?!_next|api|favicon.ico|robots.txt|sitemap.xml|.*\\..*).*)"],
};
