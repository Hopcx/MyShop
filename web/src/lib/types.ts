export type Locale = "vi" | "en";

export interface Paged<T> {
  items: T[];
  page: number;
  pageSize: number;
  total: number;
  totalPages: number;
}

export interface ProductSummary {
  id: string;
  slug: string;
  name: string;
  brand: string;
  categorySlug: string;
  price: number;
  compareAtPrice: number | null;
  currency: string;
  image: string | null;
  rating: number;
  ratingCount: number;
  inStock: boolean;
}

export interface Variant {
  id: string;
  sku: string;
  name: string;
  color: string | null;
  size: string | null;
  price: number;
  compareAtPrice: number | null;
  available: number;
}

export interface ProductDetail {
  id: string;
  slug: string;
  name: string;
  description: string;
  brand: string;
  categorySlug: string;
  categoryName: string;
  currency: string;
  rating: number;
  ratingCount: number;
  variants: Variant[];
  images: { url: string; alt: string }[];
  colors: string[];
  sizes: string[];
}

export interface Category {
  id: string;
  slug: string;
  name: string;
  parentId: string | null;
}

export interface CartLine {
  id: string;
  variantId: string;
  productId: string;
  productSlug: string;
  productName: string;
  variantName: string;
  sku: string;
  image: string | null;
  unitPrice: number;
  quantity: number;
  lineTotal: number;
  available: number;
}

export interface Cart {
  id: string;
  currency: string;
  items: CartLine[];
  itemCount: number;
  subtotal: number;
}

export interface Quote {
  currency: string;
  subtotal: number;
  discount: number;
  shippingFee: number;
  total: number;
  appliedPromotion: string | null;
}

export type OrderStatus = "Created" | "Confirmed" | "Shipping" | "Completed" | "Cancelled";

export interface Order {
  id: string;
  number: string;
  status: OrderStatus;
  paymentMethod: "Cod" | "MockPay";
  paymentStatus: "Pending" | "Succeeded" | "Failed" | "Refunded" | null;
  email: string;
  currency: string;
  subtotal: number;
  discount: number;
  shippingFee: number;
  total: number;
  promotionCode: string | null;
  shippingAddress: Address;
  createdAt: string;
  items: { productId: string; sku: string; productName: string; variantName: string; unitPrice: number; quantity: number; lineTotal: number }[];
  history: { from: OrderStatus; to: OrderStatus; note: string; at: string }[];
  trackingNumber: string | null;
}

export interface Address {
  id?: string;
  fullName: string;
  phone: string;
  line1: string;
  ward?: string;
  district?: string;
  city: string;
  country?: string;
  isDefault?: boolean;
}

export interface User {
  id: string;
  email: string;
  fullName: string;
  role: "Customer" | "Admin";
  preferredLocale: Locale;
}

export interface Review {
  id: string;
  author: string;
  rating: number;
  comment: string;
  verifiedPurchase: boolean;
  createdAt: string;
}

export interface ApiProblem {
  title?: string;
  status?: number;
  code?: string;
  errors?: Record<string, string[]>;
}
