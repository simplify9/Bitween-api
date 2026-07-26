import type { ApiClient } from "../client";
import { ApiRequestError, type NotificationEntry, type Notifier, type NotifierDetail } from "../types";
import { get, post } from "./request";

interface SearchyResponse<T> {
  result: T[];
  totalCount: number;
}
interface RawKeyAndValue {
  key: string;
  value: string;
}
interface RawNotifier {
  id: number;
  name: string;
  inactive: boolean;
  handlerId: string | null;
  handlerProperties: RawKeyAndValue[] | null;
  runOnSuccessfulResult: boolean;
  runOnBadResult: boolean;
  runOnFailedResult: boolean;
  runOnSubscriptions: { id: number; name: string | null }[] | null;
}
interface RawNotification {
  xchangeId: string;
  success: boolean;
  exception: string | null;
  finishedOn: string;
}
const toRecord = (kvs: RawKeyAndValue[] | null): Record<string, string> =>
  Object.fromEntries((kvs ?? []).map((kv) => [kv.key, kv.value]));
const toKvArray = (record: Record<string, string>): RawKeyAndValue[] =>
  Object.entries(record).map(([key, value]) => ({ key, value }));

const toNotifier = (r: RawNotifier): Notifier => ({
  id: r.id,
  name: r.name,
  enabled: !r.inactive,
  onFailed: r.runOnFailedResult,
  onBadResult: r.runOnBadResult,
  onSuccess: r.runOnSuccessfulResult,
  channelId: r.handlerId ?? "",
  channelProperties: toRecord(r.handlerProperties),
  integrationIds: (r.runOnSubscriptions ?? []).map((s) => s.id),
  createdOn: "",
});

async function fetchRecentNotifications(notifierName: string): Promise<NotificationEntry[]> {
  const res = await get<SearchyResponse<RawNotification>>(
    `/notifications?filter=${encodeURIComponent(`NotifierName:1:${notifierName}`)}`,
  );
  return (res.result ?? []).map((n) => ({
    xchangeId: n.xchangeId,
    success: n.success,
    exception: n.exception ?? undefined,
    on: n.finishedOn,
  }));
}

async function fetchDetail(id: number): Promise<NotifierDetail> {
  const raw = await get<RawNotifier | null>(`/notifiers/${id}`);
  if (!raw) throw new ApiRequestError("NOT_FOUND", "This notifier no longer exists.");
  const notifier = toNotifier(raw);
  return { ...notifier, recentNotifications: await fetchRecentNotifications(notifier.name) };
}

export const notifierMethods = {
  async listNotifiers(): Promise<Notifier[]> {
    const res = await get<SearchyResponse<{ id: number }>>("/notifiers");
    return Promise.all((res.result ?? []).map((r) => fetchDetail(r.id)));
  },

  getNotifier: fetchDetail,

  async createNotifier({ name }: { name: string }): Promise<Notifier> {
    const id = await post<number>("/notifiers", { name });
    return {
      id,
      name,
      enabled: true,
      onFailed: false,
      onBadResult: false,
      onSuccess: false,
      channelId: "",
      channelProperties: {},
      integrationIds: [],
      createdOn: "",
    };
  },

  async updateNotifier(id: number, changes: Omit<Notifier, "id" | "createdOn">): Promise<Notifier> {
    await post(`/notifiers/${id}`, {
      name: changes.name,
      runOnSuccessfulResult: changes.onSuccess,
      runOnBadResult: changes.onBadResult,
      runOnFailedResult: changes.onFailed,
      handlerId: changes.channelId,
      inactive: !changes.enabled,
      handlerProperties: toKvArray(changes.channelProperties),
      runOnSubscriptions: changes.integrationIds.map((subscriptionId) => ({ id: subscriptionId })),
    });
    return { id, createdOn: "", ...changes };
  },
} satisfies Partial<ApiClient>;
