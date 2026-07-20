import { useEffect, useMemo, useState } from "react";
import { Link, useNavigate, useParams } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, Trash2, Workflow } from "lucide-react";
import { api } from "../../api";
import { Can, useSessionCan } from "../../auth/guards";
import { Badge, Button, EmptyState, LoadingBlock } from "../../components/ui/basics";
import { ConfirmDialog } from "../../components/ui/overlays";
import { KeyValueEditor, toRecord, toRows, type KvRow } from "../../components/ui/KeyValueEditor";
import { EditableTitle, Panel, UnsavedBar } from "../../components/ui/Panel";
import { IntegrationMiniList, useIntegrationsCache } from "../../components/config/shared";
import { formatDate } from "../../lib/dates";

export function GlobalValueSetPage() {
  const { id = "" } = useParams();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const canEdit = useSessionCan("global-values.edit");

  const set = useQuery({
    queryKey: ["value-set", id],
    queryFn: () => api.getValueSet(id),
    retry: false,
  });
  const integrations = useIntegrationsCache();

  const [name, setName] = useState("");
  const [rows, setRows] = useState<KvRow[] | null>(null);
  const [deleting, setDeleting] = useState(false);
  const [loaded, setLoaded] = useState(false);

  useEffect(() => {
    if (!loaded && set.data) {
      setName(set.data.name);
      setRows(toRows(set.data.values));
      setLoaded(true);
    }
  }, [set.data, loaded]);

  const dirty = useMemo(() => {
    if (!set.data || rows === null) return false;
    return name !== set.data.name || JSON.stringify(toRecord(rows)) !== JSON.stringify(set.data.values);
  }, [set.data, name, rows]);

  const save = useMutation({
    mutationFn: () => api.updateValueSet(id, { name, values: toRecord(rows ?? []) }),
    onSuccess: async () => {
      // Await the detail refetch before re-syncing the draft (avoids stale-data race).
      await queryClient.invalidateQueries({ queryKey: ["value-set", id] });
      void queryClient.invalidateQueries({ queryKey: ["value-sets"] });
      setLoaded(false);
    },
  });

  if (set.isPending) return <LoadingBlock label="Loading value set…" />;
  if (set.isError)
    return (
      <EmptyState title="This value set no longer exists">
        <Link to="/global-values" className="font-medium text-crimson-700 hover:underline">
          Back to global values
        </Link>
      </EmptyState>
    );

  const s = set.data;

  return (
    <div className="pb-24">
      <Link
        to="/global-values"
        className="mb-4 inline-flex items-center gap-1 text-[13px] font-medium text-ink-500 hover:text-ink-800"
      >
        <ArrowLeft className="size-3.5" /> Global values
      </Link>

      <div className="mb-6 flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="flex items-center gap-2.5 text-[22px] font-semibold tracking-tight text-ink-900">
            <EditableTitle value={name} onChange={setName} disabled={!canEdit} placeholder="Value set name" />
            <code className="shrink-0 rounded-md bg-ink-100 px-1.5 py-0.5 font-mono text-xs text-ink-700">
              {s.id}
            </code>
          </h1>
          <p className="mt-1 text-sm text-ink-500">Created {formatDate(s.createdOn)}.</p>
        </div>
        <Can permission="global-values.delete">
          <Button variant="danger" onClick={() => setDeleting(true)}>
            <Trash2 className="size-4" /> Delete
          </Button>
        </Can>
      </div>

      <div className="grid gap-5 lg:grid-cols-[minmax(0,1fr)_360px]">
        <div className="min-w-0 space-y-5">
          <Panel
            title="Values"
            description="Paste a reference into any adapter field to use a value."
          >
            <KeyValueEditor
              rows={rows ?? []}
              onChange={setRows}
              keyLabel="Key"
              valueLabel="Value"
              keyPlaceholder="baseUrl"
              valuePlaceholder="https://…"
              editable={canEdit}
              token={(row) => `{{globals.${s.id}.${row.key.trim()}}}`}
              emptyText="No values yet."
              rowDetails={(row) => {
                if (!row.key.trim()) return null;
                const users = (integrations.data ?? []).filter((setup) =>
                  setup.globals.some((g) => g.setId === id && g.keys.includes(row.key.trim())),
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
        </div>

        <div className="min-w-0">
          <Panel
            title="Used by"
            description="Integrations whose adapters reference this set."
          >
            {s.usedBy.length === 0 ? (
              <p className="text-sm text-ink-500">Not referenced anywhere yet — safe to delete.</p>
            ) : (
              <ul className="space-y-2.5">
                {s.usedBy.map((u) => (
                  <li key={u.integrationSetup.id} className="text-sm">
                    <div className="flex items-center gap-2.5">
                      <Workflow className="size-3.5 shrink-0 text-ink-300" aria-hidden />
                      <Link
                        to={`/subscriptions/${u.integrationSetup.id}`}
                        className="min-w-0 flex-1 truncate font-medium text-ink-800 hover:text-crimson-700 hover:underline"
                      >
                        {u.integrationSetup.name}
                      </Link>
                      <Badge>{u.integrationSetup.type}</Badge>
                    </div>
                    <p className="mt-0.5 pl-6 font-mono text-xs text-ink-500">
                      {u.keys.map((k) => `${s.id}.${k}`).join("  ·  ")}
                    </p>
                  </li>
                ))}
              </ul>
            )}
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
          title="Delete this value set?"
          body={
            <>
              <strong className="font-medium text-ink-800">{s.name}</strong> and all its values will
              be gone for good. Sets still referenced by integrations can't be deleted.
            </>
          }
          confirmLabel="Delete value set"
          onConfirm={async () => {
            await api.deleteValueSet(id);
            void queryClient.invalidateQueries({ queryKey: ["value-sets"] });
            navigate("/global-values");
          }}
          onClose={() => setDeleting(false)}
        />
      )}
    </div>
  );
}
