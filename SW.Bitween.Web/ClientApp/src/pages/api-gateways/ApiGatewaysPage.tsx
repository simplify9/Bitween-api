import { useMemo } from "react";
import { useNavigate, useSearchParams } from "react-router";
import { useQuery } from "@tanstack/react-query";
import { Plus, Search, Webhook } from "lucide-react";
import { api, type IntegrationRow } from "../../api";
import { Can } from "../../auth/guards";
import { PageHeader } from "../../components/layout/PageHeader";
import { Badge, Button, EmptyState, LoadingBlock } from "../../components/ui/basics";
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
export function ApiGatewaysPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const navigate = useNavigate();
  const q = searchParams.get("q") ?? "";

  const gateways = useQuery({ queryKey: ["api-gateways"], queryFn: () => api.listApiGateways() });
  const integrationsById = useIntegrationRowsById();

  const setQ = (value: string) =>
    setSearchParams(
      (prev) => {
        const next = new URLSearchParams(prev);
        if (value) next.set("q", value);
        else next.delete("q");
        return next;
      },
      { replace: true },
    );

  const filtered = useMemo(() => {
    const needle = q.trim().toLowerCase();
    return (gateways.data ?? []).filter(
      (g) => !needle || g.name.toLowerCase().includes(needle) || g.urlName.toLowerCase().includes(needle),
    );
  }, [gateways.data, q]);

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
          onChange={(e) => setQ(e.target.value)}
          placeholder="Search gateways"
          aria-label="Search API gateways"
          className="h-9 w-full rounded-lg border border-ink-200 bg-white pr-3 pl-9 text-sm placeholder:text-ink-400 focus:border-crimson-400 focus:ring-2 focus:ring-crimson-100 focus:outline-none"
        />
      </div>

      {gateways.isPending ? (
        <LoadingBlock label="Loading API gateways…" />
      ) : filtered.length === 0 ? (
        <EmptyState icon={<Webhook />} title={q ? "No gateways match" : "No API gateways yet"}>
          {q ? "Try a different search." : "Create a gateway to give partners a URL to push documents to."}
        </EmptyState>
      ) : (
        <Table
          rows={filtered}
          rowKey={(g) => g.id}
          minWidth="min-w-200"
          onRowClick={(g) => navigate(`/api-gateways/${g.id}`)}
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
