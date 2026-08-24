import { useState } from "react";
import { useNavigate, useSearchParams } from "react-router";
import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { FileText, Plus, Search } from "lucide-react";
import { api } from "../../api";
import { Can } from "../../auth/guards";
import { InformationTypeDialog } from "../../components/config/InformationTypeDialog";
import { PageHeader } from "../../components/layout/PageHeader";
import { Badge, Button, EmptyState, LoadingBlock } from "../../components/ui/basics";
import { CodeBadge } from "../../components/ui/Panel";
import { Pagination } from "../../components/ui/Pagination";
import { Table } from "../../components/ui/Table";
import { UsedByCell, useIntegrationsCache } from "../../components/config/shared";

const PAGE_SIZE = 25;

export function InformationTypesPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const navigate = useNavigate();
  const [creating, setCreating] = useState(false);
  const q = searchParams.get("q") ?? "";
  const offset = searchParams.get("offset") ? Number(searchParams.get("offset")) : 0;

  const types = useQuery({
    queryKey: ["information-types-search", q, offset],
    queryFn: () => api.searchInformationTypes({ search: q, offset, limit: PAGE_SIZE }),
    placeholderData: keepPreviousData,
  });
  const integrations = useIntegrationsCache().data ?? [];

  const setParam = (key: string, value: string | null, resetOffset = true) =>
    setSearchParams(
      (prev) => {
        const next = new URLSearchParams(prev);
        if (value) next.set(key, value);
        else next.delete(key);
        if (resetOffset) next.delete("offset");
        return next;
      },
      { replace: key === "q" },
    );

  const rows = types.data?.result ?? [];
  const total = types.data?.total ?? 0;

  return (
    <div>
      <PageHeader
        title="Information types"
        description="The kinds of business documents flowing between you and your partners — each with a short code used across the system."
        help={{
          title: "How information types work",
          body: (
            <>
              <p>
                Every exchange carries exactly one information type (a purchase order, a shipment
                update…). The <strong>code</strong> is its short identity everywhere in Bitween;
                the name is the friendly label.
              </p>
              <p>
                <strong>Promoted properties</strong> pull values out of the payload (by JSON path
                or XML path) so routes and filters can match on them — for example routing orders
                by store number.
              </p>
            </>
          ),
        }}
        actions={
          <Can permission="documents.create">
            <Button variant="primary" onClick={() => setCreating(true)}>
              <Plus className="size-4" /> New information type
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
          placeholder="Search by name"
          aria-label="Search information types"
          className="h-9 w-full rounded-lg border border-ink-200 bg-white pr-3 pl-9 text-sm placeholder:text-ink-400 focus:border-crimson-400 focus:ring-2 focus:ring-crimson-100 focus:outline-none"
        />
      </div>

      {types.isPending ? (
        <LoadingBlock label="Loading information types…" />
      ) : rows.length === 0 ? (
        <EmptyState icon={<FileText />} title={q ? "No information types match" : "No information types yet"}>
          {q ? "Try a different search." : "Define the first kind of document your integrations will carry."}
        </EmptyState>
      ) : (
        <Table
          rows={rows}
          rowKey={(t) => t.id}
          minWidth="min-w-220"
          onRowClick={(t) => navigate(`/information-types/${t.id}`)}
          footer={
            <Pagination
              offset={offset}
              limit={PAGE_SIZE}
              total={total}
              onOffsetChange={(o) => setParam("offset", String(o), false)}
            />
          }
          columns={[
            { header: "Code", cell: (t) => <CodeBadge code={t.code} name={t.name} /> },
            { header: "Name", cell: (t) => <span className="font-medium text-ink-900">{t.name}</span> },
            { header: "Format", cell: (t) => <Badge>{t.format.toUpperCase()}</Badge> },
            {
              header: "Bus",
              cell: (t) =>
                t.busEnabled ? (
                  <code className="font-mono text-xs text-ink-600">{t.busMessageTypeName}</code>
                ) : (
                  <span className="text-ink-400">—</span>
                ),
            },
            {
              // The promoted keys themselves — these are what exchange search
              // filters on, so knowing there are "3" of them helps nobody.
              header: "Promoted",
              truncate: true,
              cell: (t) =>
                t.promotedProperties.length > 0 ? (
                  <span
                    className="block truncate font-mono text-xs text-ink-600"
                    title={t.promotedProperties.map((p) => p.key).join(", ")}
                  >
                    {t.promotedProperties.map((p) => p.key).join(", ")}
                  </span>
                ) : (
                  <span className="text-ink-400">—</span>
                ),
            },
            {
              header: "Duplicates",
              cell: (t) => (
                <span className="text-[13px] text-ink-600">
                  {t.duplicateIntervalMinutes > 0 ? `${t.duplicateIntervalMinutes}m window` : "Off"}
                </span>
              ),
            },
            {
              header: "Used by",
              truncate: true,
              cell: (t) => <UsedByCell items={integrations.filter((s) => s.informationTypeId === t.id)} />,
            },
          ]}
        />
      )}


      {creating && (
        <InformationTypeDialog
          typeId={null}
          onClose={() => setCreating(false)}
          onSaved={({ id }) => navigate(`/information-types/${id}`)}
        />
      )}
    </div>
  );
}
