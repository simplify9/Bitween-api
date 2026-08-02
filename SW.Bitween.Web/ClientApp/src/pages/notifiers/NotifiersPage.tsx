import { useMemo, useState, type FormEvent } from "react";
import { useNavigate, useSearchParams } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { BellRing, Plus, Search } from "lucide-react";
import { api } from "../../api";
import { Can } from "../../auth/guards";
import { useAdapterCatalog } from "../../components/config/AdapterConfig";
import { PageHeader } from "../../components/layout/PageHeader";
import { Badge, Button, EmptyState, FormError, LoadingBlock } from "../../components/ui/basics";
import { Field, TextInput } from "../../components/ui/forms";
import { Dialog } from "../../components/ui/overlays";
import { Table } from "../../components/ui/Table";

function CreateNotifierDialog({ onClose }: { onClose: () => void }) {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [name, setName] = useState("");

  const create = useMutation({
    mutationFn: () => api.createNotifier({ name }),
    onSuccess: (notifier) => {
      void queryClient.invalidateQueries({ queryKey: ["notifiers"] });
      navigate(`/notifiers/${notifier.id}`);
    },
  });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    create.mutate();
  };

  return (
    <Dialog title="New notifier" onClose={onClose}>
      <form onSubmit={submit} className="space-y-4">
        <Field
          label="Name"
          htmlFor="nn-name"
          hint="Triggers, channel and watched integrations are set on the notifier's page."
        >
          <TextInput
            id="nn-name"
            required
            autoFocus
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="e.g. Ops email on failures"
          />
        </Field>
        <FormError>{create.error?.message}</FormError>
        <div className="flex justify-end gap-2">
          <Button onClick={onClose}>Cancel</Button>
          <Button type="submit" variant="primary" busy={create.isPending}>
            Create notifier
          </Button>
        </div>
      </form>
    </Dialog>
  );
}

export function NotifiersPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const navigate = useNavigate();
  const q = searchParams.get("q") ?? "";
  const creating = searchParams.get("new") === "1";

  const notifiers = useQuery({ queryKey: ["notifiers"], queryFn: () => api.listNotifiers() });
  const channels = useAdapterCatalog("handler");

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
    return (notifiers.data ?? []).filter((n) => !needle || n.name.toLowerCase().includes(needle));
  }, [notifiers.data, q]);

  const channelLabel = (id: string) => channels.data?.find((c) => c.id === id)?.label ?? id;

  return (
    <div>
      <PageHeader
        title="Notifiers"
        description="Alerts sent to your team when watched integrations fail — or succeed."
        help={{
          title: "How notifiers work",
          body: (
            <>
              <p>
                A notifier <strong>watches</strong> a set of integrations. Whenever one of them
                finishes an exchange with an outcome the notifier cares about — failed, bad result
                or success — a notification goes out through its channel (email, Teams, …).
              </p>
              <p>
                A notifier that watches no integrations never sends anything. Every delivery
                attempt is recorded on the notifier's page.
              </p>
            </>
          ),
        }}
        actions={
          <Can permission="notifiers.create">
            <Button variant="primary" onClick={() => setParam("new", "1")}>
              <Plus className="size-4" /> New notifier
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
          placeholder="Search notifiers"
          aria-label="Search notifiers"
          className="h-9 w-full rounded-lg border border-ink-200 bg-white pr-3 pl-9 text-sm placeholder:text-ink-400 focus:border-crimson-400 focus:ring-2 focus:ring-crimson-100 focus:outline-none"
        />
      </div>

      {notifiers.isPending ? (
        <LoadingBlock label="Loading notifiers…" />
      ) : filtered.length === 0 ? (
        <EmptyState icon={<BellRing />} title={q ? "No notifiers match" : "No notifiers yet"}>
          {q ? "Try a different search." : "Create a notifier to get alerted when integrations fail."}
        </EmptyState>
      ) : (
        <Table
          rows={filtered}
          rowKey={(n) => n.id}
          onRowClick={(n) => navigate(`/notifiers/${n.id}`)}
          columns={[
            { header: "Notifier", cell: (n) => <span className="font-medium text-ink-900">{n.name}</span> },
            {
              header: "Sends when",
              cell: (n) => (
                <span className="flex flex-wrap gap-1">
                  {n.onFailed && <Badge tone="danger">Failed</Badge>}
                  {n.onBadResult && <Badge tone="warn">Bad result</Badge>}
                  {n.onSuccess && <Badge tone="ok">Success</Badge>}
                  {!n.onFailed && !n.onBadResult && !n.onSuccess && <span className="text-ink-400">Never</span>}
                </span>
              ),
            },
            { header: "Channel", cell: (n) => <span className="text-ink-600">{channelLabel(n.channelId)}</span> },
            {
              header: "Watches",
              cell: (n) =>
                n.integrationIds.length > 0 ? (
                  <span className="text-ink-600">
                    {n.integrationIds.length} integration{n.integrationIds.length === 1 ? "" : "s"}
                  </span>
                ) : (
                  <span className="text-warn-700">Nothing — never fires</span>
                ),
            },
            { header: "Status", cell: (n) => (n.enabled ? <Badge tone="ok">Active</Badge> : <Badge>Off</Badge>) },
          ]}
        />
      )}

      {creating && <CreateNotifierDialog onClose={() => setParam("new", null)} />}
    </div>
  );
}
