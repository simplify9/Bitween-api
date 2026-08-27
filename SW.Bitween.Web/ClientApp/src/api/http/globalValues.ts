import type { ApiClient } from "../client";
import type { GlobalValuesSet, GlobalValuesSetDetail, GlobalValuesSetRow, SubscriptionType, ValueSetUsage } from "../types";
import { referencesGlobal, scanReferenceTokens } from "./references";
import { get, getEnrichment, post } from "./request";

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

const SUB_TYPE_BY_NUM: Record<number, SubscriptionType> = {
  1: "Internal",
  2: "ApiCall",
  4: "Receiving",
  8: "Aggregation",
  16: "GatewayApiCall",
  32: "BusGateway",
};
const SUBSCRIPTION_TYPES: SubscriptionType[] = [
  "Receiving",
  "GatewayApiCall",
  "BusGateway",
  "Internal",
  "ApiCall",
  "Aggregation",
];
/** Enums may arrive as the numeric value or the name in any case. */
const toSubscriptionType = (t: number | string): SubscriptionType => {
  if (typeof t === "number") return SUB_TYPE_BY_NUM[t] ?? "Internal";
  return SUBSCRIPTION_TYPES.find((k) => k.toLowerCase() === t.toLowerCase()) ?? "Internal";
};

// GlobalAdapterValuesSet has no CreatedOn column on the backend.
const toRow = (r: RawValueSet): GlobalValuesSet => ({
  id: r.id,
  name: r.name,
  values: r.values ?? {},
  createdOn: "",
});

async function fetchAllSubscriptionsForUsage(): Promise<RawSubscriptionForUsage[]> {
  const res = await getEnrichment<SearchyResponse<RawSubscriptionForUsage>>("/subscriptions", { result: [], totalCount: 0 });
  return res.result ?? [];
}

function globalKeysReferencedBy(sub: RawSubscriptionForUsage, setId: string): string[] {
  const { globals } = scanReferenceTokens(
    [sub.mapperProperties, sub.handlerProperties, sub.receiverProperties, sub.validatorProperties].flatMap(
      (props) => (props ?? []).map((p) => p.value),
    ),
  );
  return globals.find((g) => referencesGlobal({ globals: [g] }, setId))?.keys ?? [];
}


export const globalValuesMethods = {
  // No usage scan here: the list page answers "used by" from the subscriptions
  // cache it already holds, so a second full /subscriptions fetch would be waste.
  async listValueSets(): Promise<GlobalValuesSetRow[]> {
    const res = await get<SearchyResponse<RawValueSet>>("/globaladaptervaluessets");
    return (res.result ?? []).map(toRow);
  },

  async getValueSet(id: string): Promise<GlobalValuesSetDetail> {
    const [r, subs] = await Promise.all([
      get<RawValueSet>(`/globaladaptervaluessets/${id}`),
      fetchAllSubscriptionsForUsage(),
    ]);
    const usedBy: ValueSetUsage[] = subs
      .map((s) => ({
        subscriptionSetup: { id: s.id, name: s.name, type: toSubscriptionType(s.type) },
        keys: globalKeysReferencedBy(s, id),
      }))
      .filter((u) => u.keys.length > 0);
    return { ...toRow(r), usedBy };
  },

  async createValueSet(input: { id: string; name: string; values: Record<string, string> }): Promise<GlobalValuesSetRow> {
    await post("/globaladaptervaluessets", { id: input.id, name: input.name, values: input.values });
    return toRow(input);
  },

  async updateValueSet(
    id: string,
    changes: { name: string; values: Record<string, string> },
  ): Promise<GlobalValuesSetRow> {
    await post(`/globaladaptervaluessets/${id}`, { name: changes.name, values: changes.values });
    return toRow({ id, ...changes });
  },

  async deleteValueSet(id: string): Promise<void> {
    await post(`/globaladaptervaluessets/${id}/delete`, {});
  },
} satisfies Partial<ApiClient>;
