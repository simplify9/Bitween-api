import type { AuditQuery, Paged, TrailEntry } from "../types";
import { get } from "./request";

interface RawAuditEntry {
  id: string;
  correlationId: string;
  sequence: number;
  occurredOn: string;
  userId: string | null;
  userDisplayName: string | null;
  /** What the change came through, when not the account's own session: "Claude via MCP", say. */
  via?: string | null;
  entityName: string;
  entityKey: string;
  state: "Added" | "Modified" | "Deleted";
  changes: Record<string, { old: unknown; new: unknown }> | null;
}

interface SearchyResponse<T> {
  result: T[];
  totalCount: number;
}

const toEntry = (r: RawAuditEntry): TrailEntry => ({
  id: r.id,
  on: r.occurredOn,
  action: r.state,
  // The backend resolves the name on read, so a deleted account leaves the id showing
  // rather than a name that is no longer true.
  // An AI app's changes say so beside its member's name.
  by: (r.userDisplayName ?? (r.userId ? `#${r.userId}` : "System")) + (r.via ? ` (${r.via})` : ""),
  byUserId: r.userDisplayName ? (r.userId ?? undefined) : undefined,
  entityName: r.entityName,
  entityKey: r.entityKey,
  correlationId: r.correlationId,
  changes: Object.entries(r.changes ?? {}).map(([property, diff]) => ({
    property,
    old: diff.old,
    new: diff.new,
  })),
});

function buildQuery(query: AuditQuery): string {
  const params = new URLSearchParams();
  params.set("offset", String(query.offset));
  params.set("limit", String(query.limit));
  if (query.entityName) params.set("entityName", query.entityName);
  if (query.entityKey) params.set("entityKey", query.entityKey);
  if (query.userId) params.set("userId", query.userId);
  if (query.correlationId) params.set("correlationId", query.correlationId);
  if (query.from) params.set("from", query.from);
  if (query.to) params.set("to", query.to);
  return params.toString();
}

export async function searchAudit(query: AuditQuery): Promise<Paged<TrailEntry>> {
  const res = await get<SearchyResponse<RawAuditEntry>>(`/audit?${buildQuery(query)}`);
  return { result: (res.result ?? []).map(toEntry), total: res.totalCount ?? 0 };
}

export const auditMethods = { searchAudit };
