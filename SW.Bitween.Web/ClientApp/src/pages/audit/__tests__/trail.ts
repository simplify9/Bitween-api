import { http, HttpResponse } from "msw";
import { apiPath } from "../../../__tests__/support/renderApp";

/** One row of the trail as `GET /audit` sends it (AuditEntryModel). */
export interface AuditRow {
  id: string;
  correlationId: string;
  sequence: number;
  occurredOn: string;
  userId: string | null;
  userDisplayName: string | null;
  entityName: string;
  entityKey: string;
  state: "Added" | "Modified" | "Deleted";
  changes: Record<string, { old: unknown; new: unknown }>;
}

let next = 1;

export const auditRow = (row: Partial<AuditRow> & Pick<AuditRow, "entityName" | "entityKey">): AuditRow => ({
  id: `audit-${next++}`,
  correlationId: "save-1",
  sequence: 0,
  occurredOn: "2026-09-20T10:00:00Z",
  userId: "9999",
  userDisplayName: "Test Admin",
  state: "Added",
  changes: { Name: { old: null, new: "Something" } },
  ...row,
});

/**
 * The trail as a tiny server: it applies the same filters `Audit/Search.cs` does, so a page that
 * narrows its query sees its list narrow, and records every query it was asked so a test can say
 * what went out on the wire.
 */
export function auditTrail(rows: AuditRow[]) {
  const asked: URLSearchParams[] = [];
  const handler = http.get(apiPath("/audit"), ({ request }) => {
    const q = new URL(request.url).searchParams;
    asked.push(q);
    const matching = rows.filter(
      (r) =>
        (!q.get("entityName") || r.entityName === q.get("entityName")) &&
        (!q.get("entityKey") || r.entityKey === q.get("entityKey")) &&
        (!q.get("userId") || r.userId === q.get("userId")) &&
        (!q.get("correlationId") || r.correlationId === q.get("correlationId")),
    );
    const offset = Number(q.get("offset") ?? 0);
    const limit = Number(q.get("limit") ?? 20);
    return HttpResponse.json({ result: matching.slice(offset, offset + limit), totalCount: matching.length });
  });
  return { handler, asked };
}

