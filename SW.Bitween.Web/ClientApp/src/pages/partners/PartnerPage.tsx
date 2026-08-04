import { useEffect, useMemo, useState } from "react";
import { Link, useNavigate, useParams } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, Trash2 } from "lucide-react";
import { api } from "../../api";
import { Can, useSessionCan } from "../../auth/guards";
import { Badge, Button, EmptyState, LoadingBlock } from "../../components/ui/basics";
import { ConfirmDialog } from "../../components/ui/overlays";
import { EditableTitle, Panel, UnsavedBar } from "../../components/ui/Panel";
import { MiniTable } from "../../components/ui/Table";
import { ExchangesList, SetupList, usePartnerIntegrations } from "../../components/config/shared";
import {
  PartnerFields,
  partnerChanges,
  partnerDirty,
  partnerDraftOf,
  type PartnerDraft,
} from "../../components/config/PartnerFields";

export function PartnerPage() {
  const { id = "" } = useParams();
  const partnerId = Number(id);
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const canEdit = useSessionCan("partners.edit");

  const partner = useQuery({
    queryKey: ["partner", partnerId],
    queryFn: () => api.getPartner(partnerId),
    retry: false,
  });
  // Keyed by partner so gateway-linked integrations are included, not just the
  // legacy ones that carry their own partnerId.
  const partnerIntegrations = usePartnerIntegrations();

  const [draft, setDraft] = useState<PartnerDraft | null>(null);
  const [deleting, setDeleting] = useState(false);
  const [loaded, setLoaded] = useState(false);

  useEffect(() => {
    if (!loaded && partner.data) {
      setDraft(partnerDraftOf(partner.data));
      setLoaded(true);
    }
  }, [partner.data, loaded]);

  const dirty = useMemo(
    () => !!partner.data && !!draft && partnerDirty(draft, partnerDraftOf(partner.data)),
    [partner.data, draft],
  );

  const save = useMutation({
    mutationFn: () => api.updatePartner(partnerId, partnerChanges(draft!)),
    onSuccess: async () => {
      // Await the detail refetch BEFORE re-syncing the draft, so the re-sync
      // effect reads the freshly-saved server data (not the stale cache).
      await queryClient.invalidateQueries({ queryKey: ["partner", partnerId] });
      void queryClient.invalidateQueries({ queryKey: ["partners"] });
      setLoaded(false);
    },
  });

  if (partner.isPending) return <LoadingBlock label="Loading partner…" />;
  if (partner.isError)
    return (
      <EmptyState title="This partner no longer exists">
        <Link to="/partners" className="font-medium text-crimson-700 hover:underline">
          Back to partners
        </Link>
      </EmptyState>
    );

  const p = partner.data;

  // Both gateway kinds in one table — a partner is reached through gateways,
  // and which mechanism it is belongs in a column, not in a separate list.
  const gatewayUses = [
    ...p.apiGateways.map((g) => ({
      key: `ag-${g.urlName}`,
      name: g.gatewayName,
      href: `/api-gateways/${g.gatewayId}`,
      detail: `/${g.urlName}`,
      kind: "API gateway",
    })),
    ...p.busGatewayRoutes.map((r, i) => ({
      key: `bg-${r.gatewayId}-${i}`,
      name: r.gatewayName,
      href: `/bus-gateways/${r.gatewayId}`,
      detail: r.matchExpression,
      kind: "Bus route",
    })),
  ];

  return (
    <div className="pb-24">
      <Link
        to="/partners"
        className="mb-4 inline-flex items-center gap-1 text-[13px] font-medium text-ink-500 hover:text-ink-800"
      >
        <ArrowLeft className="size-3.5" /> Partners
      </Link>

      <div className="mb-6 flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="flex items-center gap-2.5 text-[22px] font-semibold tracking-tight text-ink-900">
            <EditableTitle
              value={draft?.name ?? p.name}
              onChange={(name) => setDraft((d) => (d ? { ...d, name } : d))}
              disabled={!canEdit || p.isSystem}
              placeholder="Partner name"
            />
            {p.isSystem && <Badge tone="ink">Built-in</Badge>}
          </h1>
          {p.isSystem && (
            <p className="mt-1 text-sm text-ink-500">
              The built-in partner Bitween uses internally.
            </p>
          )}
        </div>
        {!p.isSystem && (
          <Can permission="partners.delete">
            <Button variant="danger" onClick={() => setDeleting(true)}>
              <Trash2 className="size-4" /> Delete partner
            </Button>
          </Can>
        )}
      </div>

      <div className="grid gap-5 lg:grid-cols-[minmax(0,1fr)_360px]">
        <div className="min-w-0">
          {draft && (
            <PartnerFields
              draft={draft}
              onChange={setDraft}
              canEdit={canEdit}
              isSystem={p.isSystem}
              partnerId={partnerId}
              credentials={p.apiCredentials}
            />
          )}
        </div>

        <div className="min-w-0 space-y-5">
          <Panel title="Used by" description="Everything that references this partner.">
            <div className="space-y-4">
              {/* Not p.integrationSetups: Partners/Get returns only the partner's
                  own subscriptions, so a partner reached through a gateway read
                  "Not used by any integration" while the row below listed the route. */}
              <SetupList items={partnerIntegrations.get(partnerId) ?? []} />
              {gatewayUses.length > 0 && (
                <div className="border-t border-ink-100 pt-3">
                  <MiniTable
                    rows={gatewayUses}
                    rowKey={(g) => g.key}
                    empty=""
                    columns={[
                      {
                        header: "Gateway",
                        truncate: true,
                        cell: (g) => (
                          <Link
                            to={g.href}
                            className="block truncate font-medium text-ink-800 hover:text-crimson-700 hover:underline"
                          >
                            {g.name}
                          </Link>
                        ),
                      },
                      {
                        header: "Match",
                        truncate: true,
                        cell: (g) => (
                          <code className="block truncate font-mono text-xs text-ink-500">{g.detail}</code>
                        ),
                      },
                      { header: "Kind", align: "right", cell: (g) => <Badge>{g.kind}</Badge> },
                    ]}
                  />
                </div>
              )}
            </div>
          </Panel>

          <Can permission="exchanges.view">
            <Panel title="Recent exchanges" description="Latest traffic involving this partner.">
              <ExchangesList items={p.recentExchanges} hide={["partner"]} />
            </Panel>
          </Can>
        </div>
      </div>

      {canEdit && dirty && (
        <UnsavedBar
          busy={save.isPending}
          error={save.error?.message}
          onSave={() => save.mutate()}
          onDiscard={() => setLoaded(false)}
        />
      )}

      {deleting && (
        <ConfirmDialog
          title="Delete this partner?"
          body={
            <>
              <strong className="font-medium text-ink-800">{p.name}</strong>, its properties and its
              API keys will be gone for good.
            </>
          }
          confirmLabel="Delete partner"
          onConfirm={async () => {
            await api.deletePartner(partnerId);
            void queryClient.invalidateQueries({ queryKey: ["partners"] });
            navigate("/partners");
          }}
          onClose={() => setDeleting(false)}
        />
      )}
    </div>
  );
}
