import { useEffect, useMemo, useState } from "react";
import { Link, useNavigate, useParams } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, Cable, History, Trash2 } from "lucide-react";
import { api, type InformationType, type InformationTypeFormat } from "../../api";
import { Can, useSessionCan } from "../../auth/guards";
import { Badge, Button, EmptyState, LoadingBlock } from "../../components/ui/basics";
import { Checkbox, Field, Select, TextInput } from "../../components/ui/forms";
import { ConfirmDialog } from "../../components/ui/overlays";
import { KeyValueEditor, type KvRow } from "../../components/ui/KeyValueEditor";
import { CodeBadge, Panel, UnsavedBar } from "../../components/ui/Panel";
import { ExchangesList, SetupList } from "../../components/config/shared";
import { ReturnBanner } from "../../components/ui/ReturnBanner";
import { formatDate } from "../../lib/dates";

type Draft = Omit<InformationType, "id" | "createdOn" | "promotedProperties">;

export function InformationTypePage() {
  const { id = "" } = useParams();
  const typeId = Number(id);
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const canEdit = useSessionCan("documents.edit");

  const type = useQuery({
    queryKey: ["information-type", typeId],
    queryFn: () => api.getInformationType(typeId),
    retry: false,
  });

  const [draft, setDraft] = useState<Draft | null>(null);
  const [propRows, setPropRows] = useState<KvRow[]>([]);
  const [deleting, setDeleting] = useState(false);
  const [loaded, setLoaded] = useState(false);

  useEffect(() => {
    if (!loaded && type.data) {
      const { id: _id, createdOn: _c, promotedProperties, ...rest } = type.data;
      setDraft({
        name: rest.name,
        code: rest.code,
        format: rest.format,
        busEnabled: rest.busEnabled,
        busMessageTypeName: rest.busMessageTypeName,
        duplicateIntervalMinutes: rest.duplicateIntervalMinutes,
        disregardsUnfilteredMessages: rest.disregardsUnfilteredMessages,
      });
      setPropRows(promotedProperties.map((p) => ({ key: p.key, value: p.path })));
      setLoaded(true);
    }
  }, [type.data, loaded]);

  const dirty = useMemo(() => {
    if (!type.data || !draft) return false;
    const t = type.data;
    return (
      draft.name !== t.name ||
      draft.code !== t.code ||
      draft.format !== t.format ||
      draft.busEnabled !== t.busEnabled ||
      (draft.busMessageTypeName ?? "") !== (t.busMessageTypeName ?? "") ||
      draft.duplicateIntervalMinutes !== t.duplicateIntervalMinutes ||
      draft.disregardsUnfilteredMessages !== t.disregardsUnfilteredMessages ||
      JSON.stringify(propRows) !==
        JSON.stringify(t.promotedProperties.map((p) => ({ key: p.key, value: p.path })))
    );
  }, [type.data, draft, propRows]);

  const save = useMutation({
    mutationFn: () =>
      api.updateInformationType(typeId, {
        ...draft!,
        promotedProperties: propRows
          .filter((r) => r.key.trim() || r.value.trim())
          .map((r) => ({ key: r.key, path: r.value })),
      }),
    onSuccess: async () => {
      // Await the detail refetch before re-syncing the draft (avoids stale-data race).
      await queryClient.invalidateQueries({ queryKey: ["information-type", typeId] });
      void queryClient.invalidateQueries({ queryKey: ["information-types"] });
      setLoaded(false);
    },
  });

  if (type.isPending) return <LoadingBlock label="Loading information type…" />;
  if (type.isError)
    return (
      <EmptyState title="This information type no longer exists">
        <Link to="/information-types" className="font-medium text-crimson-700 hover:underline">
          Back to information types
        </Link>
      </EmptyState>
    );

  const t = type.data;
  const set = <K extends keyof Draft>(key: K, value: Draft[K]) =>
    setDraft((d) => (d ? { ...d, [key]: value } : d));

  return (
    <div className="pb-24">
      <Link
        to="/information-types"
        className="mb-4 inline-flex items-center gap-1 text-[13px] font-medium text-ink-500 hover:text-ink-800"
      >
        <ArrowLeft className="size-3.5" /> Information types
      </Link>

      <ReturnBanner />

      <div className="mb-6 flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="flex items-center gap-2.5 text-[22px] font-semibold tracking-tight text-ink-900">
            {t.name}
            <CodeBadge code={t.code} name={t.name} />
          </h1>
          <p className="mt-1 text-sm text-ink-500">Defined {formatDate(t.createdOn)}.</p>
        </div>
        <Can permission="documents.delete">
          <Button variant="danger" onClick={() => setDeleting(true)}>
            <Trash2 className="size-4" /> Delete
          </Button>
        </Can>
      </div>

      <div className="grid gap-5 lg:grid-cols-[minmax(0,1fr)_360px]">
        <div className="min-w-0 space-y-5">
          <Panel title="Definition">
            {draft && (
              <div className="grid gap-4 sm:grid-cols-2">
                <Field label="Name" htmlFor="it-name">
                  <TextInput
                    id="it-name"
                    value={draft.name}
                    disabled={!canEdit}
                    onChange={(e) => set("name", e.target.value)}
                  />
                </Field>
                <Field
                  label="Code"
                  htmlFor="it-code"
                  hint="Optional. Renaming it changes how it appears everywhere; existing integrations keep working."
                >
                  <TextInput
                    id="it-code"
                    value={draft.code ?? ""}
                    disabled={!canEdit}
                    onChange={(e) => set("code", e.target.value.toUpperCase() || undefined)}
                    className="font-mono"
                    placeholder="None set"
                  />
                </Field>
                <Field label="Payload format" htmlFor="it-format">
                  <Select
                    id="it-format"
                    value={draft.format}
                    disabled={!canEdit}
                    onChange={(e) => set("format", e.target.value as InformationTypeFormat)}
                    options={[
                      { value: "Json", label: "JSON" },
                      { value: "Xml", label: "XML" },
                    ]}
                  />
                </Field>
                <Field
                  label="Duplicate window (minutes)"
                  htmlFor="it-dup"
                  hint="Identical payloads arriving within this window are treated as duplicates. 0 turns it off."
                >
                  <TextInput
                    id="it-dup"
                    type="number"
                    min={0}
                    value={draft.duplicateIntervalMinutes}
                    disabled={!canEdit}
                    onChange={(e) => set("duplicateIntervalMinutes", Math.max(0, Number(e.target.value)))}
                  />
                </Field>
                <div className="space-y-3 sm:col-span-2">
                  <Checkbox
                    label="Available on the message bus"
                    description="Lets bus gateways listen for this type."
                    checked={draft.busEnabled}
                    disabled={!canEdit}
                    onChange={(e) => set("busEnabled", e.target.checked)}
                  />
                  {draft.busEnabled && (
                    <div className="max-w-sm pl-6">
                      <Field label="Bus message type name" htmlFor="it-bus" hint="Must be unique across information types.">
                        <TextInput
                          id="it-bus"
                          value={draft.busMessageTypeName ?? ""}
                          disabled={!canEdit}
                          onChange={(e) => set("busMessageTypeName", e.target.value)}
                          className="font-mono"
                          placeholder="purchase-order"
                        />
                      </Field>
                    </div>
                  )}
                  <Checkbox
                    label="Disregard unfiltered messages"
                    description="Drop bus messages of this type that no route matches, instead of failing them."
                    checked={draft.disregardsUnfilteredMessages}
                    disabled={!canEdit}
                    onChange={(e) => set("disregardsUnfilteredMessages", e.target.checked)}
                  />
                </div>
              </div>
            )}
          </Panel>

          <Panel
            title="Promoted properties"
            description={`Values pulled out of each payload by ${t.format === "Json" ? "JSON path" : "XML path"} — routes and filters match on them.`}
          >
            <KeyValueEditor
              rows={propRows}
              onChange={setPropRows}
              keyLabel="Friendly name"
              valueLabel={draft?.format === "Xml" ? "XML path" : "JSON path"}
              keyPlaceholder="OrderNumber"
              valuePlaceholder={draft?.format === "Xml" ? "//Order/Number" : "$.order.id"}
              editable={canEdit}
              emptyText="No promoted properties — routes can only match on the whole payload."
            />
          </Panel>
        </div>

        <div className="min-w-0 space-y-5">
          <Panel title="Used by" description="Everything that carries or routes this type.">
            <div className="space-y-4">
              <SetupList items={t.integrationSetups} />
              {t.busGateways.length > 0 && (
                <ul className="space-y-1.5 border-t border-ink-100 pt-3">
                  {t.busGateways.map((g) => (
                    <li key={g.gatewayId} className="flex items-center gap-2.5 text-sm">
                      <Cable className="size-3.5 shrink-0 text-ink-300" aria-hidden />
                      <Link
                        to={`/bus-gateways/${g.gatewayId}`}
                        className="min-w-0 flex-1 truncate font-medium text-ink-800 hover:text-crimson-700 hover:underline"
                      >
                        {g.gatewayName}
                      </Link>
                      <Badge>Bus gateway</Badge>
                    </li>
                  ))}
                </ul>
              )}
            </div>
          </Panel>

          <Can permission="exchanges.view">
            <Panel
              title="Recent exchanges"
              description="Expand a row to see its input, mapped and handled documents."
            >
              <ExchangesList items={t.recentExchanges} expandable />
            </Panel>
          </Can>

          <Panel title="History">
            <ul className="space-y-2">
              {[...t.trail].reverse().map((entry, i) => (
                <li key={i} className="flex items-center gap-2.5 text-sm">
                  <History className="size-3.5 shrink-0 text-ink-300" aria-hidden />
                  <span className="min-w-0 flex-1 truncate text-ink-600">
                    <span className="font-medium text-ink-800">{entry.action}</span> by{" "}
                    {entry.byUserId ? (
                      <Link to={`/team/members/${entry.byUserId}`} className="hover:text-crimson-700 hover:underline">
                        {entry.by}
                      </Link>
                    ) : (
                      entry.by
                    )}
                  </span>
                  <span className="shrink-0 text-xs text-ink-400">{formatDate(entry.on)}</span>
                </li>
              ))}
            </ul>
          </Panel>
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
          title="Delete this information type?"
          body={
            <>
              <strong className="font-medium text-ink-800">{t.code ?? t.name}</strong> and its promoted
              properties will be gone for good. Types still used by integrations can't be deleted.
            </>
          }
          confirmLabel="Delete information type"
          onConfirm={async () => {
            await api.deleteInformationType(typeId);
            void queryClient.invalidateQueries({ queryKey: ["information-types"] });
            navigate("/information-types");
          }}
          onClose={() => setDeleting(false)}
        />
      )}
    </div>
  );
}
