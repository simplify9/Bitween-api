import { useEffect, useMemo, useState, type FormEvent } from "react";
import { Link, useNavigate, useParams } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, Plus, Trash2 } from "lucide-react";
import { api, referencesPartnerProp } from "../../api";
import { Can, useSessionCan } from "../../auth/guards";
import { Badge, Button, EmptyState, FormError, LoadingBlock } from "../../components/ui/basics";
import { Field, TextInput } from "../../components/ui/forms";
import { ConfirmDialog, Dialog } from "../../components/ui/overlays";
import { CopyField } from "../../components/ui/CopyField";
import { KeyValueEditor, toRecord, toRows, type KvRow } from "../../components/ui/KeyValueEditor";
import { EditableTitle, Panel, UnsavedBar } from "../../components/ui/Panel";
import { MiniTable } from "../../components/ui/Table";
import {
  ExchangesList,
  IntegrationMiniList,
  SetupList,
  usePartnerIntegrations,
} from "../../components/config/shared";
import { ReturnBanner } from "../../components/ui/ReturnBanner";

function AddKeyDialog({ partnerId, onClose }: { partnerId: number; onClose: () => void }) {
  const queryClient = useQueryClient();
  const [name, setName] = useState("");
  const [issuedKey, setIssuedKey] = useState("");

  const add = useMutation({
    mutationFn: () => api.addPartnerCredential(partnerId, name),
    onSuccess: ({ key }) => {
      setIssuedKey(key);
      void queryClient.invalidateQueries({ queryKey: ["partner", partnerId] });
      void queryClient.invalidateQueries({ queryKey: ["partners"] });
    },
  });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    add.mutate();
  };

  if (issuedKey) {
    return (
      <Dialog title="API key created" onClose={onClose}>
        <div className="space-y-4">
          <CopyField value={issuedKey} label={`Key "${name}"`} />
          <p className="text-[13px] text-ink-500">
            Copy it now and share it with the partner — for security, the full key is never shown
            again.
          </p>
          <div className="flex justify-end">
            <Button variant="primary" onClick={onClose}>
              Done
            </Button>
          </div>
        </div>
      </Dialog>
    );
  }

  return (
    <Dialog title="New API key" onClose={onClose}>
      <form onSubmit={submit} className="space-y-4">
        <Field label="Key name" htmlFor="ak-name" hint="What this key is for — the partner may hold several.">
          <TextInput
            id="ak-name"
            required
            autoFocus
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="e.g. Production"
          />
        </Field>
        <FormError>{add.error?.message}</FormError>
        <div className="flex justify-end gap-2">
          <Button onClick={onClose}>Cancel</Button>
          <Button type="submit" variant="primary" busy={add.isPending}>
            Generate key
          </Button>
        </div>
      </form>
    </Dialog>
  );
}

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

  const [name, setName] = useState("");
  const [propRows, setPropRows] = useState<KvRow[] | null>(null);
  const [addingKey, setAddingKey] = useState(false);
  const [revoking, setRevoking] = useState<string | null>(null);
  const [deleting, setDeleting] = useState(false);
  const [loaded, setLoaded] = useState(false);

  useEffect(() => {
    if (!loaded && partner.data) {
      setName(partner.data.name);
      setPropRows(toRows(partner.data.adapterProperties));
      setLoaded(true);
    }
  }, [partner.data, loaded]);

  const dirty = useMemo(() => {
    if (!partner.data || propRows === null) return false;
    return (
      name !== partner.data.name ||
      JSON.stringify(toRecord(propRows)) !== JSON.stringify(partner.data.adapterProperties)
    );
  }, [partner.data, name, propRows]);

  const save = useMutation({
    mutationFn: () =>
      api.updatePartner(partnerId, {
        name: name !== partner.data?.name ? name : undefined,
        adapterProperties: toRecord(propRows ?? []),
      }),
    onSuccess: async () => {
      // Await the detail refetch BEFORE re-syncing the draft, so the re-sync
      // effect reads the freshly-saved server data (not the stale cache).
      await queryClient.invalidateQueries({ queryKey: ["partner", partnerId] });
      void queryClient.invalidateQueries({ queryKey: ["partners"] });
      setLoaded(false);
    },
  });

  const revoke = useMutation({
    mutationFn: (keyName: string) => api.revokePartnerCredential(partnerId, keyName),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["partner", partnerId] });
      void queryClient.invalidateQueries({ queryKey: ["partners"] });
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

      <ReturnBanner />

      <div className="mb-6 flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="flex items-center gap-2.5 text-[22px] font-semibold tracking-tight text-ink-900">
            <EditableTitle
              value={name}
              onChange={setName}
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
        <div className="min-w-0 space-y-5">
          <Panel
            title="Properties"
            description="Key-value settings adapters can reference — use the reference token inside any adapter field."
          >
            <KeyValueEditor
              rows={propRows ?? []}
              onChange={setPropRows}
              keyLabel="Property"
              valueLabel="Value"
              keyPlaceholder="storeId"
              valuePlaceholder="CR-114"
              editable={canEdit}
              token={(row) => `{{partner.${row.key.trim()}}}`}
              emptyText="No properties yet."
              rowDetails={(row) => {
                if (!row.key.trim()) return null;
                const users = (partnerIntegrations.get(partnerId) ?? []).filter((s) =>
                  referencesPartnerProp(s, row.key.trim()),
                );
                return (
                  <IntegrationMiniList
                    items={users}
                    emptyText="Not referenced by any integration — safe to change or remove."
                  />
                );
              }}
            />
          </Panel>

          <Panel
            title="API keys"
            description="Credentials partners use to call Bitween's gateways."
            action={
              <Can permission="partners.edit">
                <Button size="sm" onClick={() => setAddingKey(true)}>
                  <Plus className="size-3.5" /> New key
                </Button>
              </Can>
            }
          >
            <MiniTable
              rows={p.apiCredentials}
              rowKey={(c) => c.name}
              empty="No API keys yet."
              columns={[
                {
                  header: "Key",
                  truncate: true,
                  cell: (c) => <span className="block truncate font-medium text-ink-800">{c.name}</span>,
                },
                { header: "Prefix", cell: (c) => <code className="font-mono text-xs text-ink-500">{c.keyPrefix}…</code> },
                {
                  header: "",
                  align: "right",
                  cell: (c) => (
                    <Can permission="partners.edit">
                      <Button size="sm" variant="danger" onClick={() => setRevoking(c.name)}>
                        Revoke
                      </Button>
                    </Can>
                  ),
                },
              ]}
            />
            <FormError>{revoke.error?.message}</FormError>
          </Panel>
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
              <ExchangesList items={p.recentExchanges} />
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

      {addingKey && <AddKeyDialog partnerId={partnerId} onClose={() => setAddingKey(false)} />}

      {revoking && (
        <ConfirmDialog
          title="Revoke this API key?"
          body={
            <>
              Anything the partner calls with the key{" "}
              <strong className="font-medium text-ink-800">{revoking}</strong> will stop
              authenticating immediately.
            </>
          }
          confirmLabel="Revoke key"
          onConfirm={async () => {
            await revoke.mutateAsync(revoking);
          }}
          onClose={() => setRevoking(null)}
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
