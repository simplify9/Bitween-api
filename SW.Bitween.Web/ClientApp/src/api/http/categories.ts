import type { ApiClient } from "../client";
import type { SubscriptionCategory } from "../types";
import { get, post } from "./request";

/** Every category there is: a short list, read whole. */
const ALL = 500;

export const categoryMethods = {
  async listCategories(): Promise<SubscriptionCategory[]> {
    const res = await get<{ result: SubscriptionCategory[] | null }>(`/subscriptioncategories?limit=${ALL}`);
    return (res.result ?? []).sort((a, b) => a.code.localeCompare(b.code));
  },
  createCategory: (input: { code: string; description: string }) => post<number>("/subscriptioncategories", input),
  async updateCategory(id: number, input: { code: string; description: string }) {
    await post(`/subscriptioncategories/${id}`, input);
  },
  async deleteCategory(id: number) {
    await post(`/subscriptioncategories/${id}/delete`, {});
  },
} satisfies Partial<ApiClient>;
