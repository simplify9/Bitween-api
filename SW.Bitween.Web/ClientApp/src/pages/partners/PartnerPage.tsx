import { useEffect, useMemo, useState, type FormEvent } from "react";
import { Link, useNavigate, useParams } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, KeyRound, Plus, Trash2, Webhook } from "lucide-react";
import { api } from "../../api";
import { Can, useSessionCan } from "../../auth/guards";
import { Badge, Button, EmptyState, FormError, LoadingBlock } from "../../components/ui/basics";
import { Field, TextInput } from "../../components/ui/forms";
import { ConfirmDialog, Dialog } from "../../components/ui/overlays";
import { CopyField } from "../../components/ui/CopyField";
import { KeyValueEditor, toRecord, toRows, type KvRow } from "../../components/ui/KeyValueEditor";
import { EditableTitle, Panel, UnsavedBar } from "../../components/ui/Panel";
import {
  ExchangesList,
  IntegrationMiniList,
  SetupList,
  useIntegrationsCache,
} from "../../components/config/shared";
import { ReturnBanner } from "../../components/ui/ReturnBanner";
import { formatDate } from "../../lib/dates";

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
  const integrations = useIntegrationsCache();

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
          <p className="mt-1 text-sm text-ink-500">
            Partner since {formatDate(p.createdOn)}.
            {p.isSystem && " The built-in partner Bitween uses internally."}
          </p>
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
                const users = (integrations.data ?? []).filter(
                  (s) => s.partnerIds.includes(partnerId) && s.partnerPropKeys.includes(row.key.trim()),
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
            {p.apiCredentials.length === 0 ? (
              <p className="text-sm text-ink-500">No API keys yet.</p>
            ) : (
              <ul className="divide-y divide-ink-100">
                {p.apiCredentials.map((c) => (
                  <li key={c.name} className="flex items-center gap-3 py-2 first:pt-0 last:pb-0">
                    <KeyRound className="size-3.5 shrink-0 text-ink-300" aria-hidden />
                    <span className="min-w-0 flex-1 truncate text-sm font-medium text-ink-800">
                      {c.name}
                    </span>
                    <code className="font-mono text-xs text-ink-500">{c.keyPrefix}…</code>
                    <span className="hidden text-xs text-ink-400 sm:block">{formatDate(c.createdOn)}</span>
                    <Can permission="partners.edit">
                      <Button size="sm" variant="danger" onClick={() => setRevoking(c.name)}>
                        Revoke
                      </Button>
                    </Can>
                  </li>
                ))}
              </ul>
            )}
            <FormError>{revoke.error?.message}</FormError>
          </Panel>
        </div>

        <div className="min-w-0 space-y-5">
          <Panel title="Used by" description="Everything that references this partner.">
            <div className="space-y-4">
              <SetupList items={p.integrationSetups} />
              {p.apiGateways.length > 0 && (
                <ul className="space-y-1.5 border-t border-ink-100 pt-3">
                  {p.apiGateways.map((g) => (
                    <li key={g.urlName} className="flex items-center gap-2.5 text-sm">
                      <Webhook className="size-3.5 shrink-0 text-ink-300" aria-hidden />
                      <Link
                        to={`/api-gateways/${g.gatewayId}`}
                        className="min-w-0 flex-1 truncate font-medium text-ink-800 hover:text-crimson-700 hover:underline"
                      >
                        {g.gatewayName}
                      </Link>
                      <code className="font-mono text-xs text-ink-500">/{g.urlName}</code>
                      <Badge>API gateway</Badge>
                    </li>
                  ))}
                </ul>
              )}
              {p.busGatewayRoutes.length > 0 && (
                <ul className="space-y-1.5 border-t border-ink-100 pt-3">
                  {p.busGatewayRoutes.map((r, i) => (
                    <li key={i} className="flex items-center gap-2.5 text-sm">
                      <Link
                        to={`/bus-gateways/${r.gatewayId}`}
                        className="min-w-0 flex-1 truncate font-medium text-ink-800 hover:text-crimson-700 hover:underline"
                      >
                        {r.gatewayName}
                      </Link>
                      <code className="truncate font-mono text-xs text-ink-500">{r.matchExpression}</code>
                      <Badge>Bus route</Badge>
                    </li>
                  ))}
                </ul>
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
