import type { ApiClient } from "../client";
import type { GlobalValuesSet, GlobalValuesSetDetail, GlobalValuesSetRow, IntegrationType, ValueSetUsage } from "../types";
import { get, post } from "./request";

interface SearchyResponse<T> {
  result: T[];
  totalCount: number;
}
interface RawValueSet {
  id: string;
  name: string;
  values: Record<string, string> | null;
}
interface RawKeyAndValue {
  key: string;
  value: string;
}
interface RawSubscriptionForUsage {
  id: number;
  name: string;
  type: number | string;
  mapperProperties: RawKeyAndValue[] | null;
  handlerProperties: RawKeyAndValue[] | null;
  receiverProperties: RawKeyAndValue[] | null;
  validatorProperties: RawKeyAndValue[] | null;
}

const SUB_TYPE_BY_NUM: Record<number, IntegrationType> = {
  1: "Internal",
  2: "ApiCall",
  4: "Receiving",
  8: "Aggregation",
  16: "GatewayApiCall",
  32: "BusGateway",
};
const INTEGRATION_TYPES: IntegrationType[] = [
  "Receiving",
  "GatewayApiCall",
  "BusGateway",
  "Internal",
  "ApiCall",
  "Aggregation",
];
/** Enums may arrive as the numeric value or the name in any case. */
const toIntegrationType = (t: number | string): IntegrationType => {
  if (typeof t === "number") return SUB_TYPE_BY_NUM[t] ?? "Internal";
  return INTEGRATION_TYPES.find((k) => k.toLowerCase() === t.toLowerCase()) ?? "Internal";
};

// GlobalAdapterValuesSet has no CreatedOn column on the backend.
const toRow = (r: RawValueSet): GlobalValuesSet => ({
  id: r.id,
  name: r.name,
  values: r.values ?? {},
  createdOn: "",
});

async function fetchAllSubscriptionsForUsage(): Promise<RawSubscriptionForUsage[]> {
  const res = await get<SearchyResponse<RawSubscriptionForUsage>>("/subscriptions");
  return res.result ?? [];
}

/**
 * Backend token resolution (StartupValuesFiller.Fill) splits on the first "."
 * only and puts no character-class restriction on the set id or key, so match
 * the same way here rather than a restrictive charset.
 */
const GLOBAL_TOKEN_RE = /\{\{globals\.([^.]+)\.([^}]+)\}\}/g;

function globalKeysReferencedBy(sub: RawSubscriptionForUsage, setId: string): string[] {
  const keys = new Set<string>();
  const values = [
    ...(sub.mapperProperties ?? []),
    ...(sub.handlerProperties ?? []),
    ...(sub.receiverProperties ?? []),
    ...(sub.validatorProperties ?? []),
  ];
  for (const { value } of values) {
    if (!value) continue;
    for (const m of value.matchAll(GLOBAL_TOKEN_RE)) {
      if (m[1] === setId) keys.add(m[2]);
    }
  }
  return [...keys];
}

export const globalValuesMethods = {
  async listValueSets(): Promise<GlobalValuesSetRow[]> {
    const [res, subs] = await Promise.all([
      get<SearchyResponse<RawValueSet>>("/globaladaptervaluessets"),
      fetchAllSubscriptionsForUsage(),
    ]);
    return (res.result ?? []).map((r) => ({
      ...toRow(r),
      usedByCount: subs.filter((s) => globalKeysReferencedBy(s, r.id).length > 0).length,
    }));
  },

  async getValueSet(id: string): Promise<GlobalValuesSetDetail> {
    const [r, subs] = await Promise.all([
      get<RawValueSet>(`/globaladaptervaluessets/${id}`),
      fetchAllSubscriptionsForUsage(),
    ]);
    const usedBy: ValueSetUsage[] = subs
      .map((s) => ({
        integrationSetup: { id: s.id, name: s.name, type: toIntegrationType(s.type) },
        keys: globalKeysReferencedBy(s, id),
      }))
      .filter((u) => u.keys.length > 0);
    return { ...toRow(r), usedBy };
  },

  async createValueSet(input: { id: string; name: string; values: Record<string, string> }): Promise<GlobalValuesSetRow> {
    await post("/globaladaptervaluessets", { id: input.id, name: input.name, values: input.values });
    return { ...toRow(input), usedByCount: 0 };
  },

  async updateValueSet(
    id: string,
    changes: { name: string; values: Record<string, string> },
  ): Promise<GlobalValuesSetRow> {
    await post(`/globaladaptervaluessets/${id}`, { name: changes.name, values: changes.values });
    return { ...toRow({ id, ...changes }), usedByCount: 0 };
  },

  async deleteValueSet(id: string): Promise<void> {
    await post(`/globaladaptervaluessets/${id}/delete`, {});
  },
} satisfies Partial<ApiClient>;
