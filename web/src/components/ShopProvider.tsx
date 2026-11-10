"use client";

import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import { useRouter } from "next/navigation";
import { api, tokens } from "@/lib/client-api";
import type { Cart, User } from "@/lib/types";

interface ShopState {
  user: User | null;
  cart: Cart | null;
  currency: string;
  ready: boolean;
  setCurrency(c: string): void;
  login(email: string, password: string): Promise<void>;
  register(email: string, password: string, fullName: string): Promise<void>;
  logout(): Promise<void>;
  addToCart(variantId: string, quantity: number): Promise<void>;
  updateLine(itemId: string, quantity: number): Promise<void>;
  refreshCart(): Promise<void>;
  resetCart(): void;
}

const Ctx = createContext<ShopState | null>(null);
const CART_KEY = "ms_cart";

function readCookie(name: string) {
  return document.cookie.split("; ").find((c) => c.startsWith(`${name}=`))?.split("=")[1];
}

export function ShopProvider({ children }: { children: ReactNode }) {
  const router = useRouter();
  const [user, setUser] = useState<User | null>(null);
  const [cart, setCart] = useState<Cart | null>(null);
  const [currency, setCurrencyState] = useState("VND");
  const [ready, setReady] = useState(false);

  const loadCart = useCallback(async (cur: string) => {
    const id = localStorage.getItem(CART_KEY);
    if (!id) return setCart(null);
    try {
      setCart(await api<Cart>(`/api/v1/carts/${id}?currency=${cur}`));
    } catch {
      localStorage.removeItem(CART_KEY);
      setCart(null);
    }
  }, []);

  useEffect(() => {
    const cur = readCookie("currency") ?? "VND";
    // eslint-disable-next-line react-hooks/set-state-in-effect -- đồng bộ state từ cookie khi mount
    setCurrencyState(cur);
    (async () => {
      if (tokens.refresh) {
        try {
          setUser(await api<User>("/api/v1/me"));
        } catch {
          tokens.clear();
        }
      }
      await loadCart(cur);
      setReady(true);
    })();
  }, [loadCart]);

  const ensureCart = useCallback(async () => {
    const existing = localStorage.getItem(CART_KEY);
    if (existing) return existing;
    const created = await api<Cart>(`/api/v1/carts?currency=${currency}`, { method: "POST" });
    localStorage.setItem(CART_KEY, created.id);
    return created.id;
  }, [currency]);

  const afterAuth = useCallback(
    async (data: { accessToken: string; refreshToken: string; user: User }) => {
      tokens.set(data.accessToken, data.refreshToken);
      setUser(data.user);
      // Gộp giỏ guest vào giỏ tài khoản.
      const guestCart = localStorage.getItem(CART_KEY);
      if (guestCart) {
        try {
          const merged = await api<Cart>(`/api/v1/carts/${guestCart}/merge?currency=${currency}`, { method: "POST" });
          localStorage.setItem(CART_KEY, merged.id);
          setCart(merged);
          return;
        } catch {
          localStorage.removeItem(CART_KEY);
        }
      }
      const mine = await api<Cart>(`/api/v1/carts?currency=${currency}`, { method: "POST" });
      localStorage.setItem(CART_KEY, mine.id);
      setCart(mine);
    },
    [currency],
  );

  const value = useMemo<ShopState>(
    () => ({
      user,
      cart,
      currency,
      ready,
      setCurrency(c) {
        document.cookie = `currency=${c}; path=/; max-age=31536000; samesite=lax`;
        setCurrencyState(c);
        void loadCart(c);
        router.refresh();
      },
      async login(email, password) {
        await afterAuth(await api("/api/v1/auth/login", { method: "POST", json: { email, password } }));
      },
      async register(email, password, fullName) {
        await afterAuth(await api("/api/v1/auth/register", { method: "POST", json: { email, password, fullName } }));
      },
      async logout() {
        const refresh = tokens.refresh;
        if (refresh) await api("/api/v1/auth/logout", { method: "POST", json: { refreshToken: refresh } }).catch(() => {});
        tokens.clear();
        localStorage.removeItem(CART_KEY);
        setUser(null);
        setCart(null);
      },
      async addToCart(variantId, quantity) {
        const id = await ensureCart();
        setCart(await api<Cart>(`/api/v1/carts/${id}/items?currency=${currency}`, { method: "POST", json: { variantId, quantity } }));
      },
      async updateLine(itemId, quantity) {
        if (!cart) return;
        setCart(
          await api<Cart>(`/api/v1/carts/${cart.id}/items/${itemId}?currency=${currency}`, { method: "PATCH", json: { quantity } }),
        );
      },
      refreshCart: () => loadCart(currency),
      resetCart() {
        localStorage.removeItem(CART_KEY);
        setCart(null);
      },
    }),
    [user, cart, currency, ready, loadCart, router, afterAuth, ensureCart],
  );

  return <Ctx.Provider value={value}>{children}</Ctx.Provider>;
}

export function useShop() {
  const ctx = useContext(Ctx);
  if (!ctx) throw new Error("useShop must be used inside <ShopProvider>");
  return ctx;
}
