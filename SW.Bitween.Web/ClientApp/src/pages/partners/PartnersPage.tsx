import { useMemo, useState } from "react";
import { useNavigate, useSearchParams } from "react-router";
import { useQuery } from "@tanstack/react-query";
import { Handshake, Plus, Search } from "lucide-react";
import { api } from "../../api";
import { Can } from "../../auth/guards";
import { PartnerDialog } from "../../components/config/PartnerDialog";
import { PageHeader } from "../../components/layout/PageHeader";
import { Badge, Button, EmptyState, LoadingBlock } from "../../components/ui/basics";
import { Table } from "../../components/ui/Table";
import { UsedByCell, usePartnerIntegrations } from "../../components/config/shared";

export function PartnersPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const navigate = useNavigate();
  const [creating, setCreating] = useState(false);
  const q = searchParams.get("q") ?? "";

  const partners = useQuery({ queryKey: ["partners"], queryFn: () => api.listPartners() });
  const partnerIntegrations = usePartnerIntegrations();

  const setParam = (key: string, value: string | null) =>
    setSearchParams(
      (prev) => {
        const next = new URLSearchParams(prev);
        if (value) next.set(key, value);
        else next.delete(key);
        return next;
      },
      { replace: key === "q" },
    );

  const filtered = useMemo(() => {
    const needle = q.trim().toLowerCase();
    return (partners.data ?? []).filter((p) => !needle || p.name.toLowerCase().includes(needle));
  }, [partners.data, q]);

  return (
    <div>
      <PageHeader
        title="Partners"
        description="The external parties you exchange data with — their connection properties and API keys."
        actions={
          <Can permission="partners.create">
            <Button variant="primary" onClick={() => setCreating(true)}>
              <Plus className="size-4" /> New partner
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
          placeholder="Search partners"
          aria-label="Search partners"
          className="h-9 w-full rounded-lg border border-ink-200 bg-white pr-3 pl-9 text-sm placeholder:text-ink-400 focus:border-crimson-400 focus:ring-2 focus:ring-crimson-100 focus:outline-none"
        />
      </div>

      {partners.isPending ? (
        <LoadingBlock label="Loading partners…" />
      ) : filtered.length === 0 ? (
        <EmptyState icon={<Handshake />} title={q ? "No partners match" : "No partners yet"}>
          {q ? "Try a different search." : "Create the first partner you exchange data with."}
        </EmptyState>
      ) : (
        <Table
          rows={filtered}
          rowKey={(p) => p.id}
          minWidth="min-w-200"
          onRowClick={(p) => navigate(`/partners/${p.id}`)}
          columns={[
            {
              header: "Partner",
              cell: (p) => (
                <span className="flex items-center gap-2 font-medium text-ink-900">
                  {p.name}
                  {p.isSystem && <Badge tone="ink">Built-in</Badge>}
                </span>
              ),
            },
            {
              // The property names themselves, not a count — a count tells you
              // nothing you can act on, the keys are what adapters reference.
              header: "Properties",
              truncate: true,
              cell: (p) =>
                p.propertyKeys.length > 0 ? (
                  <span
                    className="block truncate font-mono text-xs text-ink-600"
                    title={p.propertyKeys.join(", ")}
                  >
                    {p.propertyKeys.join(", ")}
                  </span>
                ) : (
                  <span className="text-ink-400">—</span>
                ),
            },
            {
              header: "API keys",
              align: "right",
              cell: (p) => <span className="tabular-nums text-ink-600">{p.credentialCount || "—"}</span>,
            },
            {
              header: "Used by",
              truncate: true,
              cell: (p) => <UsedByCell items={partnerIntegrations.get(p.id) ?? []} />,
            },
          ]}
        />
      )}


      {creating && (
        <PartnerDialog
          partnerId={null}
          onClose={() => setCreating(false)}
          onSaved={(id) => navigate(`/partners/${id}`)}
        />
      )}
    </div>
  );
}
