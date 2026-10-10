import { useMemo } from "react";
import { Link, useSearchParams } from "react-router";
import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { X } from "lucide-react";
import { api, type AuditQuery } from "../../api";
import { PageHeader } from "../../components/layout/PageHeader";
import { Button, EmptyState, LoadingBlock } from "../../components/ui/basics";
import { TextInput } from "../../components/ui/forms";
import { SearchSelect } from "../../components/ui/SearchSelect";
import { Table } from "../../components/ui/Table";
import { ChangedCell } from "../../components/config/shared";
import { formatDateTime, timeAgo } from "../../lib/dates";
import { keys } from "../../api/queryKeys";
import { entityLabel } from "../../lib/auditLabels";

const PAGE_SIZE = 25;

/**
 * The entity names the backend records, as the audit policy admits them. Kept as a list
 * rather than derived from the rows on screen, so the filter offers everything auditable
 * instead of only what the current page happens to show.
 */
const ENTITY_NAMES = [
  "Subscription",
  "Schedule",
  "SubscriptionCategory",
  "Partner",
  "ApiCredential",
  "Document",
  "ApiGateway",
  "ApiGatewayPartner",
  "BusGateway",
  "BusGatewayRoute",
  "WorkGroup",
  "RetryPolicy",
  "RetryAlertOverride",
  "Notifier",
  "GlobalAdapterValuesSet",
  "Setting",
  "Account",
  "Role",
  "AccountRoleLink",
  "OperatorAction",
  "AdapterDraft",
  "AdapterRelease",
  "AdapterSourceAccess",
];



/** Where a recorded row lives in the UI, where it has a page of its own. */
const ENTITY_LINK: Record<string, (key: string) => string> = {
  Subscription: (k) => `/subscriptions/${k}`,
  Partner: (k) => `/partners/${k}`,
  Document: (k) => `/information-types/${k}`,
  ApiGateway: (k) => `/api-gateways/${k}`,
  BusGateway: (k) => `/bus-gateways/${k}`,
  WorkGroup: (k) => `/work-groups/${k}`,
  RetryPolicy: (k) => `/retry-policies/${k}`,
  Notifier: (k) => `/notifiers/${k}`,
  Account: (k) => `/team/members/${k}`,
};

const ACTION_STYLE: Record<string, string> = {
  Added: "bg-emerald-50 text-emerald-700",
  Modified: "bg-amber-50 text-amber-700",
  Deleted: "bg-rose-50 text-rose-700",
};

const readQuery = (sp: URLSearchParams): AuditQuery => ({
  entityName: sp.get("entityName") ?? undefined,
  entityKey: sp.get("entityKey") ?? undefined,
  userId: sp.get("userId") ?? undefined,
  correlationId: sp.get("correlationId") ?? undefined,
  from: sp.get("from") ?? undefined,
  to: sp.get("to") ?? undefined,
  offset: readOffset(sp.get("offset")),
  limit: PAGE_SIZE,
});

/** `Number("bad")` is NaN, which would go out on the wire as `offset=NaN`. */
function readOffset(raw: string | null): number {
  const n = Number(raw);
  return raw && Number.isFinite(n) && n > 0 ? Math.floor(n) : 0;
}

const FILTER_KEYS = ["entityName", "entityKey", "userId", "correlationId", "from", "to"];

/**
 * Every configuration change, newest first. Recorded by the change tracker rather than by
 * each handler, so a create, an edit and a delete all land here without anyone having to
 * remember to write one.
 */
export function AuditPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const query = useMemo(() => readQuery(searchParams), [searchParams]);

  const { data, isLoading } = useQuery({
    queryKey: keys.audit.search(searchParams.toString()),
    queryFn: () => api.searchAudit(query),
    placeholderData: keepPreviousData,
  });

  const users = useQuery({ queryKey: keys.users.list, queryFn: () => api.listUsers() }).data ?? [];

  const setParam = (key: string, value: string | null, resetOffset = true) => {
    const next = new URLSearchParams(searchParams);
    if (value === null || value === "") next.delete(key);
    else next.set(key, value);
    if (resetOffset) next.delete("offset");
    setSearchParams(next, { replace: true });
  };

  const activeFilterCount = FILTER_KEYS.filter((k) => searchParams.has(k)).length;

  const rows = data?.result ?? [];
  const total = data?.total ?? 0;

  return (
    <div>
      <PageHeader
        title="Audit trail"
        description="Who changed what, and when, across every configuration entity."
        help={{
          title: "What is recorded here",
          body: (
            <>
              Every create, edit and delete of a configuration entity — subscriptions, partners,
              information types, gateways, work groups, retry policies, notifiers, settings,
              members and roles. Exchanges and other runtime traffic are deliberately excluded:
              there are far too many of them, and they are already visible on the Exchanges page.
              <br />
              <br />
              Credentials never appear. Adapter property bags, API keys and passwords are
              excluded from the record, so a change to a subscription shows that it was edited
              without showing what its handler's password became.
            </>
          ),
        }}
      />

      {/* — filters — */}
      <div className="mb-4 grid grid-cols-2 gap-2 md:grid-cols-5">
        <SearchSelect
          aria-label="Filter by entity type"
          size="sm"
          clearLabel="Any entity"
          value={query.entityName ?? ""}
          onChange={(v) => setParam("entityName", v || null)}
          options={ENTITY_NAMES.map((n) => ({ value: n, label: entityLabel(n) }))}
        />
        <SearchSelect
          aria-label="Filter by member"
          size="sm"
          clearLabel="Anyone"
          value={query.userId ?? ""}
          onChange={(v) => setParam("userId", v || null)}
          options={users.map((u) => ({ value: u.id, label: u.displayName }))}
        />
        <TextInput
          aria-label="Filter by entity id"
          className="!h-8 text-[13px]"
          placeholder="Entity id…"
          defaultValue={query.entityKey ?? ""}
          key={`key-${query.entityKey ?? ""}`}
          onBlur={(e) => e.target.value !== (query.entityKey ?? "") && setParam("entityKey", e.target.value || null)}
          onKeyDown={(e) => e.key === "Enter" && setParam("entityKey", e.currentTarget.value || null)}
        />
        <TextInput
          aria-label="Changes from this date"
          type="date"
          className="!h-8 text-[13px]"
          defaultValue={query.from ?? ""}
          key={`from-${query.from ?? ""}`}
          onChange={(e) => setParam("from", e.target.value || null)}
        />
        <TextInput
          aria-label="Changes up to this date"
          type="date"
          className="!h-8 text-[13px]"
          defaultValue={query.to ?? ""}
          key={`to-${query.to ?? ""}`}
          onChange={(e) => setParam("to", e.target.value || null)}
        />
      </div>

      {activeFilterCount > 0 && (
        <div className="mb-3 flex items-center gap-2">
          {query.correlationId && (
            <span className="rounded bg-ink-100 px-2 py-1 text-xs text-ink-600">
              Showing one save only
            </span>
          )}
          <button
            onClick={() => setSearchParams(new URLSearchParams(), { replace: true })}
            className="inline-flex h-8 items-center gap-1 rounded-lg px-2 text-[13px] font-medium text-crimson-700 hover:bg-crimson-50"
          >
            <X className="size-3.5" aria-hidden />
            Clear filters
          </button>
        </div>
      )}

      {isLoading ? (
        <LoadingBlock />
      ) : rows.length === 0 ? (
        <EmptyState title="Nothing recorded yet">
          {activeFilterCount > 0
            ? "No changes match these filters."
            : "Once someone creates or edits a subscription, partner or any other configuration, it appears here."}
        </EmptyState>
      ) : (
        <Table
          rows={rows}
          rowKey={(r) => r.id}
          minWidth="min-w-240"
          columns={[
            {
              header: "When",
              headerTitle: "When the change was saved.",
              className: "whitespace-nowrap",
              cell: (r) => (
                <>
                  <span className="font-medium text-ink-800">{timeAgo(r.on)}</span>
                  <span className="block text-xs text-ink-500">{formatDateTime(r.on)}</span>
                </>
              ),
            },
            {
              header: "Action",
              headerTitle: "Whether the row was created, changed or deleted.",
              cell: (r) => (
                <span
                  className={`inline-block rounded px-1.5 py-0.5 text-xs font-medium ${ACTION_STYLE[r.action] ?? "bg-ink-100 text-ink-600"}`}
                >
                  {r.action}
                </span>
              ),
            },
            {
              header: "Entity",
              headerTitle: "The kind of thing that changed, and which one.",
              cell: (r) => {
                const to = ENTITY_LINK[r.entityName]?.(r.entityKey);
                return (
                  <>
                    <span className="block text-[13px] font-medium text-ink-800" title={r.entityName}>
                      {entityLabel(r.entityName)}
                    </span>
                    {to && r.action !== "Deleted" ? (
                      <Link
                        to={to}
                        className="font-mono text-xs text-ink-500 hover:text-crimson-700 hover:underline"
                      >
                        {r.entityKey}
                      </Link>
                    ) : (
                      <span
                        className="font-mono text-xs text-ink-500"
                        title={r.action === "Deleted" ? "This row no longer exists." : undefined}
                      >
                        {r.entityKey}
                      </span>
                    )}
                  </>
                );
              },
            },
            {
              header: "Changed",
              headerTitle: "The fields this change touched. Open one to see the values.",
              wrap: true,
              cell: (r) => <ChangedCell changes={r.changes} entity={r.entityName} />,
            },
            {
              header: "By",
              headerTitle: "The member signed in when the change was saved.",
              wrap: true,
              cell: (r) =>
                r.byUserId ? (
                  <Link
                    to={`/team/members/${r.byUserId}`}
                    className="text-[13px] text-ink-600 hover:text-crimson-700 hover:underline"
                  >
                    {r.by}
                  </Link>
                ) : (
                  <span
                    className="text-[13px] text-ink-500"
                    title="No signed-in user — a scheduled job or a bus consumer."
                  >
                    {r.by}
                  </span>
                ),
            },
            {
              header: "",
              headerTitle: "Everything else the same save changed.",
              align: "right",
              cell: (r) => (
                <button
                  onClick={() => setParam("correlationId", r.correlationId)}
                  className="text-xs font-medium text-crimson-700 hover:underline"
                  title="Show every change saved at the same moment as this one: one edit often changes several rows."
                >
                  Saved together
                </button>
              ),
            },
          ]}
          footer={
            <div className="flex items-center justify-between border-t border-ink-100 px-4 py-2.5 text-[13px] text-ink-500">
              <span>
                Showing {query.offset + 1}–{Math.min(query.offset + PAGE_SIZE, total)} of {total}
              </span>
              <span className="flex gap-1.5">
                <Button
                  size="sm"
                  disabled={query.offset === 0}
                  onClick={() => setParam("offset", String(Math.max(0, query.offset - PAGE_SIZE)), false)}
                >
                  Previous
                </Button>
                <Button
                  size="sm"
                  disabled={query.offset + PAGE_SIZE >= total}
                  onClick={() => setParam("offset", String(query.offset + PAGE_SIZE), false)}
                >
                  Next
                </Button>
              </span>
            </div>
          }
        />
      )}
    </div>
  );
}
