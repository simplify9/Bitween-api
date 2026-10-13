import type { ApiClient } from "../client";
import type { Connection, ConnectionInput } from "../types";
import { get, post, request } from "./request";

interface SearchyResponse<T> {
  result: T[];
  totalCount: number;
}

export const connectionMethods = {
  async listConnections(): Promise<Connection[]> {
    return (await get<SearchyResponse<Connection>>("/connections")).result;
  },

  getConnection(id: number): Promise<Connection> {
    return get<Connection>(`/connections/${id}`);
  },

  createConnection(input: ConnectionInput): Promise<Connection> {
    return post<Connection>("/connections", input);
  },

  updateConnection(id: number, input: ConnectionInput): Promise<Connection> {
    return post<Connection>(`/connections/${id}`, input);
  },

  async deleteConnection(id: number): Promise<void> {
    await request(`/connections/${id}`, { method: "DELETE" });
  },

  /** The provider's approval page, for a connection someone approves once. */
  async authorizeConnection(id: number): Promise<string> {
    return (await post<{ url: string }>(`/connections/${id}/authorize`, {})).url;
  },

  /** Finishes an approval with what the provider sent the browser back with. */
  completeConnection(code: string, state: string): Promise<{ id: number; name: string }> {
    return post<{ id: number; name: string }>("/connections/complete", { code, state });
  },

  /** Renews the token now, and says how it went. */
  testConnection(id: number): Promise<Connection> {
    return post<Connection>(`/connections/${id}/test`, {});
  },
} satisfies Partial<ApiClient>;
