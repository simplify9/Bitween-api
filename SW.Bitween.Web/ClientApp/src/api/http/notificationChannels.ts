import type { ApiClient } from "../client";
import type { NotificationChannelDetail, NotificationChannelRow } from "../types";
import { get, post, request } from "./request";

interface SearchyResponse<T> {
  result: T[];
  totalCount: number;
}

export const notificationChannelMethods = {
  async listNotificationChannels(): Promise<NotificationChannelRow[]> {
    const res = await get<SearchyResponse<NotificationChannelRow>>("/notificationchannels?page=0&size=500");
    return res.result ?? [];
  },

  /**
   * Id and name only. Readable by anyone signed in, because every page that picks a channel —
   * a subscription's notifications, a retry policy's alerts — needs the names whoever edits it.
   */
  async lookupNotificationChannels(): Promise<{ id: number; name: string }[]> {
    const res = await get<Record<string, string>>("/notificationchannels?lookup=true");
    return Object.entries(res ?? {})
      .map(([id, name]) => ({ id: Number(id), name }))
      .sort((a, b) => a.name.localeCompare(b.name));
  },

  getNotificationChannel: (id: number) => get<NotificationChannelDetail>(`/notificationchannels/${id}`),

  async createNotificationChannel(input: {
    name: string;
    handlerId: string;
    handlerProperties: Record<string, string>;
  }): Promise<number> {
    return post<number>("/notificationchannels", input);
  },

  async updateNotificationChannel(
    id: number,
    input: { name: string; handlerId: string; handlerProperties: Record<string, string> },
  ): Promise<void> {
    await post(`/notificationchannels/${id}`, input);
  },

  async deleteNotificationChannel(id: number): Promise<void> {
    await request(`/notificationchannels/${id}`, { method: "DELETE" });
  },
} satisfies Partial<ApiClient>;
