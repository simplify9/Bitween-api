import type { ApiClient } from "../client";
import type {
  NotificationChannelDetail,
  NotificationChannelRow,
  NotificationChannelSend,
  NotificationChannelTestResult,
  Paged,
} from "../types";
import { get, post, request } from "./request";
import { buildListQuery, SEARCHY_RULE } from "./searchQuery";

interface SearchyResponse<T> {
  result: T[];
  totalCount: number;
}

interface RawSend {
  id: number;
  xchangeId: string;
  notifierName: string;
  success: boolean;
  exception: string | null;
  finishedOn: string;
  subscriptionId: number | null;
  subscriptionName: string | null;
}

export const notificationChannelMethods = {
  async listNotificationChannels(): Promise<NotificationChannelRow[]> {
    const res = await get<SearchyResponse<NotificationChannelRow>>("/notificationchannels?page=0&size=500");
    return res.result ?? [];
  },

  /**
   * Id and name only. Readable by anyone signed in, because every page that picks a channel -
   * a subscription's notifications, a retry policy's alerts - needs the names whoever edits it.
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

  testNotificationChannel: (id: number) =>
    post<NotificationChannelTestResult>(`/notificationchannels/${id}/test`, {}),

  async listNotificationChannelSends(
    id: number,
    query: { failedOnly: boolean; offset: number; limit: number },
  ): Promise<Paged<NotificationChannelSend>> {
    const qs = buildListQuery({
      filters: [
        ["ChannelId", SEARCHY_RULE.equalsTo, id],
        ["Success", SEARCHY_RULE.equalsTo, query.failedOnly ? "false" : ""],
      ],
      offset: query.offset,
      limit: query.limit,
    });
    const res = await get<SearchyResponse<RawSend>>(`/notifications?${qs}`);
    return {
      total: res.totalCount,
      result: (res.result ?? []).map((n) => ({
        id: n.id,
        xchangeId: n.xchangeId,
        success: n.success,
        exception: n.exception,
        on: n.finishedOn,
        sentFor: n.notifierName,
        subscriptionId: n.subscriptionId,
        subscriptionName: n.subscriptionName,
      })),
    };
  },
} satisfies Partial<ApiClient>;
