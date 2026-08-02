import { useEffect, useMemo, useState } from "react";
import { Link, useParams } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, ArrowUpRight, Search } from "lucide-react";
import { api, type NotificationEntry, type Notifier } from "../../api";
import { useSessionCan } from "../../auth/guards";
import { Badge, EmptyState, LoadingBlock } from "../../components/ui/basics";
import { Checkbox, Field, TextInput } from "../../components/ui/forms";
import { EditableTitle, Panel, UnsavedBar } from "../../components/ui/Panel";
import { MiniTable } from "../../components/ui/Table";
import { SearchSelect } from "../../components/ui/SearchSelect";
import { useAdapterCatalog } from "../../components/config/AdapterConfig";
import { useIntegrationsCache } from "../../components/config/shared";
import { timeAgo } from "../../lib/dates";

type Draft = Omit<Notifier, "id" | "createdOn">;

/** Delivery history. The failure reason is a column, not a drill-down. */
function NotificationsList({ items }: { items: NotificationEntry[] }) {
  return (
    <MiniTable
      rows={items}
      rowKey={(n) => `${n.xchangeId}-${n.on}`}
      empty="Nothing sent yet."
      columns={[
        {
          header: "Exchange",
          truncate: true,
          cell: (n) => (
            <Link
              to={`/exchanges?ids=${encodeURIComponent(n.xchangeId)}`}
              className="block truncate font-mono text-xs text-ink-600 hover:text-crimson-700 hover:underline"
            >
              {n.xchangeId}
            </Link>
          ),
        },
        {
          header: "Result",
          cell: (n) => (n.success ? <Badge tone="ok">Sent</Badge> : <Badge tone="danger">Failed</Badge>),
        },
        {
          header: "Reason",
          truncate: true,
          cell: (n) =>
            n.exception ? (
              <span className="block truncate font-mono text-[11px] text-danger-700" title={n.exception}>
                {n.exception}
              </span>
            ) : (
              <span className="text-ink-400">—</span>
            ),
        },
        {
          header: "When",
          align: "right",
          className: "whitespace-nowrap",
          cell: (n) => <span className="text-xs text-ink-400">{timeAgo(n.on)}</span>,
        },
      ]}
    />
  );
}

export function NotifierPage() {
  const { id = "" } = useParams();
  const notifierId = Number(id);
  const queryClient = useQueryClient();
  const canEdit = useSessionCan("notifiers.edit");

  const notifier = useQuery({
    queryKey: ["notifier", notifierId],
    queryFn: () => api.getNotifier(notifierId),
    retry: false,
  });
  const channels = useAdapterCatalog("handler");
  const integrations = useIntegrationsCache();

  const [draft, setDraft] = useState<Draft | null>(null);
  const [loaded, setLoaded] = useState(false);
  const [watchSearch, setWatchSearch] = useState("");

  useEffect(() => {
    if (!loaded && notifier.data) {
      const { id: _id, createdOn: _c, recentNotifications: _r, ...rest } = notifier.data;
      setDraft(structuredClone(rest));
      setLoaded(true);
    }
  }, [notifier.data, loaded]);

  const dirty = useMemo(() => {
    if (!notifier.data || !draft) return false;
    const { id: _id, createdOn: _c, recentNotifications: _r, ...saved } = notifier.data;
    return JSON.stringify(draft) !== JSON.stringify(saved);
  }, [notifier.data, draft]);

  const save = useMutation({
    mutationFn: () => api.updateNotifier(notifierId, draft!),
    onSuccess: async () => {
      // Await the detail refetch before re-syncing the draft (avoids stale-data race).
      await queryClient.invalidateQueries({ queryKey: ["notifier", notifierId] });
      void queryClient.invalidateQueries({ queryKey: ["notifiers"] });
      setLoaded(false);
    },
  });

  if (notifier.isPending) return <LoadingBlock label="Loading notifier…" />;
  if (notifier.isError)
    return (
      <EmptyState title="This notifier no longer exists">
        <Link to="/notifiers" className="font-medium text-crimson-700 hover:underline">
          Back to notifiers
        </Link>
      </EmptyState>
    );

  const n = notifier.data;
  const set = <K extends keyof Draft>(key: K, value: Draft[K]) =>
    setDraft((d) => (d ? { ...d, [key]: value } : d));

  const channel = channels.data?.find((c) => c.id === draft?.channelId);
  const watchesNothing = draft !== null && draft.integrationIds.length === 0;

  const needle = watchSearch.trim().toLowerCase();
  const filteredIntegrations = (integrations.data ?? []).filter(
    (s) => !needle || s.name.toLowerCase().includes(needle),
  );

  const toggleIntegration = (setupId: number, include: boolean) =>
    setDraft((d) =>
      d
        ? {
            ...d,
            integrationIds: include
              ? [...d.integrationIds, setupId]
              : d.integrationIds.filter((x) => x !== setupId),
          }
        : d,
    );

  return (
    <div className="pb-24">
      <Link
        to="/notifiers"
        className="mb-4 inline-flex items-center gap-1 text-[13px] font-medium text-ink-500 hover:text-ink-800"
      >
        <ArrowLeft className="size-3.5" /> Notifiers
      </Link>

      <div className="mb-6 flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="flex items-center gap-2.5 text-[22px] font-semibold tracking-tight text-ink-900">
            <EditableTitle
              value={draft?.name ?? n.name}
              onChange={(value) => set("name", value)}
              disabled={!canEdit}
              placeholder="Notifier name"
            />
            {draft && (
              <button
                type="button"
                disabled={!canEdit}
                onClick={() => set("enabled", !draft.enabled)}
                title="Turn off to pause this notifier without losing its setup."
                className={`shrink-0 rounded-md px-2 py-0.5 text-xs font-medium disabled:cursor-not-allowed ${
                  draft.enabled ? "bg-ok-100 text-ok-600 hover:bg-ok-200/70" : "bg-ink-100 text-ink-700 hover:bg-ink-200"
                }`}
              >
                {draft.enabled ? "Active" : "Off"}
              </button>
            )}
          </h1>
        </div>
      </div>

      <div className="grid gap-5 lg:grid-cols-[minmax(0,1fr)_360px]">
        <div className="min-w-0 space-y-5">
          <Panel title="Send when" description="Which exchange outcomes trigger a notification.">
            {draft && (
              <div className="space-y-3">
                <Checkbox
                  label="An exchange fails"
                  description="The pipeline errored and the exchange didn't complete."
                  checked={draft.onFailed}
                  disabled={!canEdit}
                  onChange={(e) => set("onFailed", e.target.checked)}
                />
                <Checkbox
                  label="A result comes back bad"
                  description="The exchange completed, but the reply reported a failure."
                  checked={draft.onBadResult}
                  disabled={!canEdit}
                  onChange={(e) => set("onBadResult", e.target.checked)}
                />
                <Checkbox
                  label="An exchange succeeds"
                  description="For confirmations on critical flows."
                  checked={draft.onSuccess}
                  disabled={!canEdit}
                  onChange={(e) => set("onSuccess", e.target.checked)}
                />
                {!draft.onFailed && !draft.onBadResult && !draft.onSuccess && (
                  <p className="rounded-lg bg-warn-100 px-3 py-2 text-[13px] text-warn-700">
                    No outcomes selected — this notifier never sends anything.
                  </p>
                )}
              </div>
            )}
          </Panel>

          <Panel title="Channel" description="How notifications are delivered.">
            {draft && (
              <div className="space-y-4">
                <div className="max-w-sm">
                  <Field label="Deliver via" htmlFor="nf-channel">
                    <SearchSelect
                      id="nf-channel"
                      aria-label="Deliver via"
                      value={draft.channelId}
                      disabled={!canEdit || channels.isPending}
                      onChange={(v) => set("channelId", v)}
                      placeholder="Pick a handler…"
                      options={(channels.data ?? []).map((a) => ({
                        value: a.id,
                        label: a.label,
                        code: a.id,
                        hint: a.native ? "Native" : a.versions.length > 0 ? `v${a.versions.at(-1)}` : "Custom",
                      }))}
                    />
                  </Field>
                </div>
                {channel && channel.props.length > 0 && (
                  <div className="grid gap-4 sm:grid-cols-2">
                    {channel.props.map((prop) => (
                      <Field
                        key={prop.key}
                        label={prop.optional ? prop.key : `${prop.key} *`}
                        htmlFor={`nf-prop-${prop.key}`}
                        hint={prop.description}
                      >
                        <TextInput
                          id={`nf-prop-${prop.key}`}
                          type={prop.secret ? "password" : "text"}
                          value={draft.channelProperties[prop.key] ?? ""}
                          disabled={!canEdit}
                          placeholder={prop.default}
                          onChange={(e) =>
                            set("channelProperties", {
                              ...draft.channelProperties,
                              [prop.key]: e.target.value,
                            })
                          }
                        />
                      </Field>
                    ))}
                  </div>
                )}
              </div>
            )}
          </Panel>
        </div>

        <div className="min-w-0 space-y-5">
          <Panel title="Watches" description="The integrations this notifier fires for.">
            {integrations.isPending || !draft ? (
              <LoadingBlock label="Loading integrations…" />
            ) : (
              <div className="space-y-3">
                {watchesNothing && (
                  <p className="rounded-lg bg-warn-100 px-3 py-2 text-[13px] text-warn-700">
                    This notifier watches nothing, so it never sends anything. Pick at least one
                    integration.
                  </p>
                )}
                <div className="relative">
                  <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-ink-400" />
                  <input
                    type="search"
                    value={watchSearch}
                    onChange={(e) => setWatchSearch(e.target.value)}
                    placeholder="Search integrations"
                    aria-label="Search integrations"
                    className="h-9 w-full rounded-lg border border-ink-200 bg-white pr-3 pl-9 text-sm placeholder:text-ink-400 focus:border-crimson-400 focus:ring-2 focus:ring-crimson-100 focus:outline-none"
                  />
                </div>
                <ul className="max-h-80 space-y-1.5 overflow-y-auto pr-1">
                  {filteredIntegrations.map((s) => (
                    <li key={s.id} className="flex items-center gap-1">
                      <label className="flex min-w-0 flex-1 cursor-pointer items-center gap-2.5 text-sm">
                        <input
                          type="checkbox"
                          checked={draft.integrationIds.includes(s.id)}
                          disabled={!canEdit}
                          onChange={(e) => toggleIntegration(s.id, e.target.checked)}
                          className="size-4 shrink-0 cursor-pointer rounded accent-crimson-600"
                        />
                        <span className="min-w-0 flex-1 truncate font-medium text-ink-800">
                          {s.name}
                        </span>
                        <Badge>{s.type}</Badge>
                      </label>
                      <Link
                        to={`/subscriptions/${s.id}`}
                        aria-label={`Open ${s.name}`}
                        title="Open"
                        className="shrink-0 rounded-md p-1 text-ink-400 hover:bg-ink-100 hover:text-ink-700"
                      >
                        <ArrowUpRight className="size-3.5" />
                      </Link>
                    </li>
                  ))}
                  {filteredIntegrations.length === 0 && (
                    <li className="px-1 py-2 text-sm text-ink-400">No integrations match.</li>
                  )}
                </ul>
              </div>
            )}
          </Panel>

          <Panel
            title="Recent notifications"
            description="Every delivery attempt — expand a failed one to see why."
          >
            <NotificationsList items={n.recentNotifications} />
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
    </div>
  );
}
