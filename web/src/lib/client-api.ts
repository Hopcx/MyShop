"use client";

import type { ApiProblem } from "./types";

export const PUBLIC_API_URL = process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:5080";

const ACCESS = "ms_access";
const REFRESH = "ms_refresh";

/**
 * Lưu token ở sessionStorage/localStorage cho bản demo. Production khuyến nghị BFF + cookie httpOnly/SameSite=strict
 * để giảm rủi ro XSS đánh cắp token (xem docs/architecture.md).
 */
export const tokens = {
  get access() {
    return typeof window === "undefined" ? null : sessionStorage.getItem(ACCESS);
  },
  get refresh() {
    return typeof window === "undefined" ? null : localStorage.getItem(REFRESH);
  },
  set(access: string, refresh: string) {
    sessionStorage.setItem(ACCESS, access);
    localStorage.setItem(REFRESH, refresh);
  },
  clear() {
    sessionStorage.removeItem(ACCESS);
    localStorage.removeItem(REFRESH);
  },
};

export class ApiError extends Error {
  constructor(public status: number, public problem: ApiProblem) {
    super(problem.title ?? `HTTP ${status}`);
  }
}

let refreshing: Promise<boolean> | null = null;

async function tryRefresh(): Promise<boolean> {
  const refresh = tokens.refresh;
  if (!refresh) return false;
  refreshing ??= (async () => {
    try {
      const res = await fetch(`${PUBLIC_API_URL}/api/v1/auth/refresh`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ refreshToken: refresh }),
      });
      if (!res.ok) {
        tokens.clear();
        return false;
      }
      const data = await res.json();
      tokens.set(data.accessToken, data.refreshToken);
      return true;
    } finally {
      setTimeout(() => (refreshing = null), 0);
    }
  })();
  return refreshing;
}

export async function api<T>(path: string, init: RequestInit & { json?: unknown } = {}, retry = true): Promise<T> {
  const headers = new Headers(init.headers);
  headers.set("Accept", "application/json");
  if (init.json !== undefined) headers.set("Content-Type", "application/json");
  const access = tokens.access;
  if (access) headers.set("Authorization", `Bearer ${access}`);

  const res = await fetch(`${PUBLIC_API_URL}${path}`, {
    ...init,
    headers,
    body: init.json !== undefined ? JSON.stringify(init.json) : init.body,
  });

  if (res.status === 401 && retry && tokens.refresh && (await tryRefresh())) return api<T>(path, init, false);
  if (res.status === 204) return undefined as T;
  const text = await res.text();
  const data = text ? JSON.parse(text) : undefined;
  if (!res.ok) {
    const problem: ApiProblem = data ?? {};
    if (problem.errors && !problem.title?.includes(":")) {
      problem.title = Object.values(problem.errors).flat().join(" ") || problem.title;
    }
    throw new ApiError(res.status, problem);
  }
  return data as T;
}
