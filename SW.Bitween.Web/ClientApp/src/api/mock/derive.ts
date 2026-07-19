import type { IntegrationInfo, Integration, IntegrationSetupRef } from "../types";
import type { MockDb } from "./store";

const PARTNER_TOKEN = /\{\{\s*partner\.([A-Za-z0-9_.-]+)\s*\}\}/g;
const GLOBALS_TOKEN = /\{\{\s*globals\.([A-Za-z0-9_-]+)\.([A-Za-z0-9_.-]+)\s*\}\}/g;

const allPropValues = (s: Integration): string[] => [
  ...Object.values(s.receiverProperties),
  ...Object.values(s.validatorProperties),
  ...Object.values(s.mapperProperties),
  ...Object.values(s.handlerProperties),
];

/** Keys of partner properties referenced anywhere in the adapter config. */
export const partnerPropKeysOf = (s: Integration): string[] => [
  ...new Set(allPropValues(s).flatMap((v) => [...v.matchAll(PARTNER_TOKEN)].map((m) => m[1]))),
];

/** Global value references anywhere in the adapter config, grouped per set. */
export const globalRefsOf = (s: Integration): { setId: string; keys: string[] }[] => {
  const bySet = new Map<string, Set<string>>();
  for (const v of allPropValues(s)) {
    for (const m of v.matchAll(GLOBALS_TOKEN)) {
      (bySet.get(m[1]) ?? bySet.set(m[1], new Set()).get(m[1])!).add(m[2]);
    }
  }
  return [...bySet.entries()].map(([setId, keys]) => ({ setId, keys: [...keys] }));
};

/**
 * Partners an integration runs for: its own (legacy types) plus partners
 * linked through API-gateway attachments and bus-gateway routes.
 */
export const partnerIdsOf = (db: MockDb, s: Integration): number[] => {
  const ids = new Set<number>();
  if (s.partnerId !== null) ids.add(s.partnerId);
  for (const gw of db.apiGateways)
    for (const a of gw.attachments) if (a.integrationId === s.id) ids.add(a.partnerId);
  for (const gw of db.busGateways)
    for (const r of gw.routes)
      if (r.integrationId === s.id && r.partnerId !== null) ids.add(r.partnerId);
  return [...ids];
};

export const integrationInfoOf = (db: MockDb, s: Integration): IntegrationInfo => ({
  id: s.id,
  name: s.name,
  type: s.type,
  partnerIds: partnerIdsOf(db, s),
  informationTypeId: s.informationTypeId,
  workGroupId: s.workGroupId,
  retryPolicyId: s.retryPolicyId,
  partnerPropKeys: partnerPropKeysOf(s),
  globals: globalRefsOf(s),
});

export const setupRefOf = (s: Integration): IntegrationSetupRef => ({
  id: s.id,
  name: s.name,
  type: s.type,
});
