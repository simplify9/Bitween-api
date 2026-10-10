import { useMemo } from "react";
import { Link, useNavigate, useSearchParams } from "react-router";
import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { CornerDownLeft, Plus, Search } from "lucide-react";
import { api } from "../../api";
import { Can, useSessionCan } from "../../auth/guards";
import { PageHeader } from "../../components/layout/PageHeader";
import { Button, EmptyState, LoadError, LoadingBlock } from "../../components/ui/basics";
import { Select } from "../../components/ui/forms";
import { Pagination } from "../../components/ui/Pagination";
import { Table } from "../../components/ui/Table";
import {
  HealthBadge,
  LinkListCell,
  SubscriptionStatusBadges,
  useRetryPolicyNames,
  useSubscriptionsCache,
  useWorkGroupNames,
} from "../../components/config/shared";
import { keys } from "../../api/queryKeys";
import { useSearchText } from "../../lib/useSearchText";

const STATUS_OPTIONS = [
  { value: "", label: "Any status" },
  { value: "false", label: "Active" },
  { value: "true", label: "Disabled" },
];

const PAGE_SIZE = 25;

/**
 * Response subscriptions — pipelines that run on what another subscription's delivery
 * hands back. They also appear on the Subscriptions page; this page exists for the one
 * column only they have: which subscriptions feed them.
 */
export function ResponseSubscriptionsPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const navigate = useNavigate();
  const q = searchParams.get("q") ?? "";
  const inactiveParam = searchParams.get("inactive");
  const inactive = inactiveParam === "true" ? true : inactiveParam === "false" ? false : null;
  const offset = searchParams.get("offset") ? Number(searchParams.get("offset")) : 0;
  const canSeeInfoTypes = useSessionCan("documents.view");

  const rows = useQuery({
    queryKey: keys.subscriptions.rowsSearch({ type: "Response", q, inactive, offset }),
    queryFn: () =>
      api.searchSubscriptionRows({ search: q, type: "Response", inactive, offset, limit: PAGE_SIZE }),
    placeholderData: keepPreviousData,
  });
  // The list rows don't carry work group, retry policy or who points at what; the
  // subscriptions cache does, and every page already holds it.
  const cachedSetups = useSubscriptionsCache().data;
  const setups = useMemo(() => cachedSetups ?? [], [cachedSetups]);
  const setupById = useMemo(() => new Map(setups.map((s) => [s.id, s])), [setups]);
  const workGroupNames = useWorkGroupNames();
  const retryPolicyNames = useRetryPolicyNames();

  const setParam = (key: string, value: string | null, resetOffset = true) =>
    setSearchParams(
      (prev) => {
        const next = new URLSearchParams(prev);
        if (value) next.set(key, value);
        else next.delete(key);
        if (resetOffset) next.delete("offset");
        return next;
      },
      { replace: true },
    );
  const [searchText, setSearchText] = useSearchText(q, (text) => setParam("q", text || null));

  const filtered = rows.data?.result ?? [];
  const total = rows.data?.total ?? 0;

  // In the header, and in the empty list where there is nothing else to do.
  const createAction = (
    <Can permission="subscriptions.create">
      <Button variant="primary" onClick={() => navigate("/response-subscriptions/new")}>
        <Plus className="size-4" /> New response subscription
      </Button>
    </Can>
  );

  return (
    <div>
      <PageHeader
        title="Response subscriptions"
        description="Pipelines that run on what another subscription's delivery hands back — an order id, an acknowledgement, a label."
        actions={createAction}
      />

      <div className="mb-4 flex flex-wrap items-center gap-2">
        <div className="relative w-full max-w-xs">
          <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-ink-500" />
          <input
            type="search"
            value={searchText}
            onChange={(e) => setSearchText(e.target.value)}
            placeholder="Search response subscriptions"
            aria-label="Search response subscriptions"
            className="h-9 w-full rounded-lg border border-ink-200 bg-white pr-3 pl-9 text-sm placeholder:text-ink-400 focus:border-focus-400 focus:ring-2 focus:ring-focus-100 focus:outline-none"
          />
        </div>
        <div className="w-40">
          <Select
            aria-label="Filter by status"
            className="!h-8 text-[13px]"
            value={inactiveParam ?? ""}
            onChange={(e) => setParam("inactive", e.target.value || null)}
            options={STATUS_OPTIONS}
          />
        </div>
      </div>

      {rows.isPending ? (
        <LoadingBlock label="Loading response subscriptions…" />
      ) : rows.isError ? (
        <LoadError error={rows.error} what="response subscriptions" onRetry={() => void rows.refetch()} />
      ) : filtered.length === 0 ? (
        <EmptyState
          icon={<CornerDownLeft />}
          title={q || inactive !== null ? "No response subscriptions match" : "No response subscriptions yet"} action={q || inactive !== null ? undefined : createAction}
        >
          {q || inactive !== null
            ? "Try a different search or filter."
            : "Create one, then pick it in another subscription's Response step."}
        </EmptyState>
      ) : (
        <Table
          rows={filtered}
          rowKey={(r) => r.id}
          minWidth="min-w-230"
          onRowClick={(r) => navigate(`/subscriptions/${r.id}`)}
          footer={
            <Pagination
              offset={offset}
              limit={PAGE_SIZE}
              total={total}
              onOffsetChange={(o) => setParam("offset", String(o), false)}
            />
          }
          columns={[
            {
              header: "Response subscription",
              wrap: true,
              cell: (r) => <span className="block font-medium text-ink-900">{r.name}</span>,
            },
            {
              header: "Carries",
              headerTitle: "The information type of the responses it runs on.",
              cell: (r) =>
                canSeeInfoTypes ? (
                  <Link
                    to={`/information-types/${r.informationTypeId}`}
                    onClick={(e) => e.stopPropagation()}
                    className="font-mono text-xs text-ink-600 hover:text-crimson-700 hover:underline"
                  >
                    {r.informationTypeCode}
                  </Link>
                ) : (
                  <code className="font-mono text-xs text-ink-600">{r.informationTypeCode}</code>
                ),
            },
            {
              // The thing this page is for: a response subscription nothing feeds never runs,
              // and the list is the only place that says so without opening each one.
              header: "Fed by",
              headerTitle: "The subscriptions that hand it their delivery's response. With none, it never runs.",
              wrap: true,
              cell: (r) => {
                const feeders = setups.filter((s) => s.responseSubscriptionId === r.id);
                if (feeders.length === 0) return <span className="text-[13px] text-danger-700">Nothing</span>;
                return (
                  <LinkListCell
                    label="subscriptions"
                    items={feeders.map((s) => ({ key: s.id, name: s.name, href: `/subscriptions/${s.id}` }))}
                  />
                );
              },
            },
            {
              header: "Work group",
              wrap: true,
              cell: (r) => {
                const id = setupById.get(r.id)?.workGroupId ?? null;
                const name = id === null ? null : (workGroupNames.get(id) ?? null);
                if (id === null) return <span className="text-[13px] text-ink-500">Ungrouped</span>;
                return name ? (
                  <Link
                    to={`/work-groups/${id}`}
                    onClick={(e) => e.stopPropagation()}
                    className="block text-[13px] text-ink-700 hover:text-crimson-700 hover:underline"
                  >
                    {name}
                  </Link>
                ) : (
                  <span className="text-[13px] text-ink-500">—</span>
                );
              },
            },
            {
              header: "Retry policy",
              wrap: true,
              cell: (r) => {
                const id = setupById.get(r.id)?.retryPolicyId ?? null;
                const name = id === null ? null : (retryPolicyNames.get(id) ?? null);
                if (id === null) return <span className="text-[13px] text-ink-500">None</span>;
                return name ? (
                  <Link
                    to={`/retry-policies/${id}`}
                    onClick={(e) => e.stopPropagation()}
                    className="block text-[13px] text-ink-700 hover:text-crimson-700 hover:underline"
                  >
                    {name}
                  </Link>
                ) : (
                  <span className="text-[13px] text-ink-500">—</span>
                );
              },
            },
            {
              header: "Status",
              cell: (r) => (
                <span className="flex max-w-20 flex-wrap items-center gap-1">
                  <SubscriptionStatusBadges enabled={r.enabled} paused={r.paused} />
                  <HealthBadge isRunning={r.isRunning} consecutiveFailures={r.consecutiveFailures} />
                </span>
              ),
            },
            {
              header: "Last error",
              truncate: true,
              cell: (r) =>
                r.lastException ? (
                  <span
                    className="block truncate font-mono text-[11px] text-danger-700"
                    title={r.lastException}
                  >
                    {r.lastException}
                  </span>
                ) : (
                  <span className="text-ink-500">—</span>
                ),
            },
          ]}
        />
      )}
    </div>
  );
}
