import { useNavigate, useSearchParams } from "react-router";
import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { Plus, Search, Webhook } from "lucide-react";
import { api, type IntegrationRow } from "../../api";
import { Can } from "../../auth/guards";
import { PageHeader } from "../../components/layout/PageHeader";
import { Badge, Button, EmptyState, LoadingBlock } from "../../components/ui/basics";
import { Pagination } from "../../components/ui/Pagination";
import { Table } from "../../components/ui/Table";
import {
  LinkListCell,
  WiredHealthBadge,
  useIntegrationRowsById,
} from "../../components/config/shared";

/**
 * API gateways — the entry point partners push documents into. One row per
 * gateway entity, not per pipeline: the pipelines behind it are reached through
 * the attachment that names them.
 */
const PAGE_SIZE = 25;

export function ApiGatewaysPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const navigate = useNavigate();
  const q = searchParams.get("q") ?? "";
  const offset = searchParams.get("offset") ? Number(searchParams.get("offset")) : 0;

  const gateways = useQuery({
    queryKey: ["api-gateways-search", q, offset],
    queryFn: () => api.searchApiGateways({ search: q, offset, limit: PAGE_SIZE }),
    placeholderData: keepPreviousData,
  });
  const integrationsById = useIntegrationRowsById();

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

  const rows = gateways.data?.result ?? [];
  const total = gateways.data?.total ?? 0;

  return (
    <div>
      <PageHeader
        title="API gateways"
        description="URLs partners call to push documents in. Each attached partner is routed to one integration."
        actions={
          <Can permission="api-gateways.create">
            <Button variant="primary" onClick={() => navigate("/api-gateways/new")}>
              <Plus className="size-4" /> New API gateway
            </Button>
          </Can>
        }
      />

      <div className="relative mb-4 max-w-xs">
        <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-ink-400" />
        <input
          type="search"
          value={q}
          onChange={(e) => setParam("q", e.target.value || null)}
          placeholder="Search gateways"
          aria-label="Search API gateways"
          className="h-9 w-full rounded-lg border border-ink-200 bg-white pr-3 pl-9 text-sm placeholder:text-ink-400 focus:border-crimson-400 focus:ring-2 focus:ring-crimson-100 focus:outline-none"
        />
      </div>

      {gateways.isPending ? (
        <LoadingBlock label="Loading API gateways…" />
      ) : rows.length === 0 ? (
        <EmptyState icon={<Webhook />} title={q ? "No gateways match" : "No API gateways yet"}>
          {q ? "Try a different search." : "Create a gateway to give partners a URL to push documents to."}
        </EmptyState>
      ) : (
        <Table
          rows={rows}
          rowKey={(g) => g.id}
          minWidth="min-w-200"
          onRowClick={(g) => navigate(`/api-gateways/${g.id}`)}
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
              header: "Gateway",
              truncate: true,
              cell: (g) => (
                <span className="flex min-w-0 items-center gap-1.5">
                  <span className={`truncate font-medium ${g.inactive ? "text-ink-400" : "text-ink-900"}`}>
                    {g.name}
                  </span>
                  {/* Beside the name, not in the Health column: health reports on what the
                      gateway feeds, and a deactivated one feeds nothing, so it would read
                      as healthy. */}
                  {g.inactive && <Badge tone="warn">Off</Badge>}
                </span>
              ),
            },
            {
              header: "URL",
              truncate: true,
              cell: (g) => (
                <code
                  className="block truncate font-mono text-xs text-ink-600"
                  title={`/api/Gateway/${g.urlName}`}
                >
                  /api/Gateway/{g.urlName}
                </code>
              ),
            },
            {
              header: "Partners",
              truncate: true,
              cell: (g) => (
                <LinkListCell
                  label="partners"
                  items={g.attachments.map((a) => ({
                    key: a.partnerId,
                    name: a.partnerName,
                    href: `/partners/${a.partnerId}`,
                    note: <Badge>{a.integrationName}</Badge>,
                  }))}
                />
              ),
            },
            {
              // No Integrations or Handles column. Both vary per attachment, and
              // a second list beside Partners can't say which entry pairs with
              // which — that belongs in the gateway's own attachments table,
              // where a row is exactly one partner and one integration.
              // An aggregate has no such problem, so health stays.
              header: "Health",
              cell: (g) => (
                <WiredHealthBadge
                  empty="No partners"
                  rows={g.attachments
                    .map((a) => integrationsById.get(a.integrationId))
                    .filter((r): r is IntegrationRow => Boolean(r))}
                />
              ),
            },
          ]}
        />
      )}
    </div>
  );
}
