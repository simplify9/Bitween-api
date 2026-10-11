import type { IssuedServiceToken, ServiceAccount } from "../types";
import { get, post, request } from "./request";

export const serviceAccountMethods = {
  async listServiceAccounts(): Promise<ServiceAccount[]> {
    return (await get<ServiceAccount[]>("/serviceaccounts")) ?? [];
  },

  async createServiceAccount(input: { name: string; roleIds: number[] }): Promise<number> {
    return await post<number>("/serviceaccounts", input);
  },

  async updateServiceAccount(id: number, input: { name: string; roleIds: number[] }): Promise<void> {
    await post(`/serviceaccounts/${id}`, input);
  },

  async setServiceAccountDisabled(id: number, disabled: boolean): Promise<void> {
    await post(`/serviceaccounts/${id}/setDisabled`, { disabled });
  },

  async deleteServiceAccount(id: number): Promise<void> {
    await request(`/serviceaccounts/${id}`, { method: "DELETE" });
  },

  async issueServiceToken(id: number, input: { name: string; expiresInDays: number }): Promise<IssuedServiceToken> {
    return await post<IssuedServiceToken>(`/serviceaccounts/${id}/issueToken`, input);
  },

  async revokeServiceToken(id: number, tokenId: number): Promise<void> {
    await post(`/serviceaccounts/${id}/revokeToken`, { tokenId });
  },
};
