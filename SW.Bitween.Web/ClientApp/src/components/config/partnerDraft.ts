import { type Partner, type PortalAccessLevel } from "../../api";
import { toRecord, toRows } from "../ui/keyValueRows";
import type { PartnerDraft } from "./PartnerFields";

export const partnerDraftOf = (
  p: Pick<Partner, "name" | "adapterProperties" | "secretProperties" | "loginIdentity"> & {
    portalAccess?: Record<string, PortalAccessLevel>;
  },
): PartnerDraft => ({
  name: p.name,
  properties: toRows(p.adapterProperties),
  secretProperties: [...p.secretProperties],
  loginIdentity: p.loginIdentity ?? "",
  ...(p.portalAccess ? { portalAccess: { ...p.portalAccess } } : {}),
});

/** What they see of the properties still there, sorted, so a renamed or removed one doesn't count. */
const accessOf = (draft: PartnerDraft) =>
  Object.entries(draft.portalAccess ?? {})
    .filter(([k]) => draft.properties.some((r) => r.key.trim() === k))
    .sort(([a], [b]) => a.localeCompare(b));

export const partnerDirty = (draft: PartnerDraft, saved: PartnerDraft): boolean =>
  draft.name !== saved.name ||
  JSON.stringify(toRecord(draft.properties)) !== JSON.stringify(toRecord(saved.properties)) ||
  // Locking a property changes nothing about its value, so the value comparison above
  // cannot see it - without this the save bar never appears for a lock on its own.
  JSON.stringify([...draft.secretProperties].sort()) !==
    JSON.stringify([...saved.secretProperties].sort()) ||
  draft.loginIdentity.trim() !== saved.loginIdentity.trim() ||
  JSON.stringify(accessOf(draft)) !== JSON.stringify(accessOf(saved));

/** What the host sends to `updatePartner`. */
export const partnerChanges = (draft: PartnerDraft) => ({
  name: draft.name.trim(),
  adapterProperties: toRecord(draft.properties.filter((r) => r.key.trim())),
  // A lock on a property that was renamed or removed would otherwise linger forever.
  secretProperties: draft.secretProperties.filter((n) =>
    draft.properties.some((r) => r.key.trim().toLowerCase() === n.toLowerCase()),
  ),
  loginIdentity: draft.loginIdentity.trim() || null,
  ...(draft.portalAccess ? { portalAccess: Object.fromEntries(accessOf(draft)) } : {}),
});
