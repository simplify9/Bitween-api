import type { ApiClient } from "../client";
import { ApiRequestError, type NotificationEntry, type Notifier, type NotifierDetail, type Paged } from "../types";
import { get, post, request } from "./request";
import { buildListQuery, SEARCHY_RULE } from "./searchQuery";

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
/** Shape of a search-endpoint row — lighter than `RawNotifier`, no adapter properties. */
interface RawNotifierRow {
  id: number;
  name: string;
  inactive: boolean | null;
  handlerId: string | null;
  runOnSuccessfulResult: boolean | null;
  runOnBadResult: boolean | null;
  runOnFailedResult: boolean | null;
  runOnSubscriptions: number[] | null;
}
interface RawNotification {
  xchangeId: string;
  success: boolean;
  exception: string | null;
  finishedOn: string;
}
// Empty-valued properties are dropped: an adapter property with no value means
// "not set", and keeping it would make a freshly-cleared field compare unequal to
// stored data that never had the key — leaving the Save bar up after an undo.
const toRecord = (kvs: RawKeyAndValue[] | null): Record<string, string> =>
  Object.fromEntries((kvs ?? []).filter((kv) => kv.value !== "").map((kv) => [kv.key, kv.value]));
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
  subscriptionIds: (r.runOnSubscriptions ?? []).map((s) => s.id),
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
  async searchNotifiers(query: { search: string; offset: number; limit: number }): Promise<Paged<Notifier>> {
    const qs = buildListQuery({
      filters: [["Name", SEARCHY_RULE.contains, query.search.trim()]],
      offset: query.offset,
      limit: query.limit,
    });
    const res = await get<SearchyResponse<RawNotifierRow>>(`/notifiers?${qs}`);
    return {
      total: res.totalCount,
      result: (res.result ?? []).map((r) => ({
        id: r.id,
        name: r.name,
        enabled: !r.inactive,
        onFailed: !!r.runOnFailedResult,
        onBadResult: !!r.runOnBadResult,
        onSuccess: !!r.runOnSuccessfulResult,
        channelId: r.handlerId ?? "",
        channelProperties: {},
        subscriptionIds: r.runOnSubscriptions ?? [],
        createdOn: "",
      })),
    };
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
      subscriptionIds: [],
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
      runOnSubscriptions: changes.subscriptionIds.map((subscriptionId) => ({ id: subscriptionId })),
    });
    return { id, createdOn: "", ...changes };
  },

  async deleteNotifier(id: number): Promise<void> {
    await request(`/notifiers/${id}`, { method: "DELETE" });
  },
} satisfies Partial<ApiClient>;
