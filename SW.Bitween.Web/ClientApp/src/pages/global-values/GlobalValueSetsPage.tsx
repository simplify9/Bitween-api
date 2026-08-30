import { useMemo, useState, type FormEvent } from "react";
import { useNavigate, useSearchParams } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Plus, Search, SlidersHorizontal } from "lucide-react";
import { api, referencesGlobal } from "../../api";
import { Can } from "../../auth/guards";
import { SubscriptionMultiFilter } from "../../components/config/SubscriptionMultiFilter";
import { PageHeader } from "../../components/layout/PageHeader";
import { Button, EmptyState, FormError, LoadingBlock } from "../../components/ui/basics";
import { Field, TextInput } from "../../components/ui/forms";
import { Dialog } from "../../components/ui/overlays";
import { Table } from "../../components/ui/Table";
import { UsedByCell, useSubscriptionsCache } from "../../components/config/shared";
import { suggestSlug } from "../../lib/identifiers";
import { keys } from "../../api/queryKeys";

function CreateValueSetDialog({ onClose }: { onClose: () => void }) {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [name, setName] = useState("");
  const [slug, setSlug] = useState("");
  const [slugTouched, setSlugTouched] = useState(false);

  const create = useMutation({
    mutationFn: () => api.createValueSet({ id: slug, name, values: {} }),
    onSuccess: (set) => {
      void queryClient.invalidateQueries({ queryKey: keys.valueSets.all });
      navigate(`/global-values/${set.id}`);
    },
  });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    create.mutate();
  };

  return (
    <Dialog title="New value set" onClose={onClose}>
      <form onSubmit={submit} className="space-y-4">
        <Field label="Name" htmlFor="nvs-name">
          <TextInput
            id="nvs-name"
            required
            autoFocus
            value={name}
            onChange={(e) => {
              setName(e.target.value);
              if (!slugTouched) setSlug(suggestSlug(e.target.value));
            }}
            placeholder="e.g. SAP production"
          />
        </Field>
        <Field
          label="ID"
          htmlFor="nvs-id"
          hint="Used inside adapter references: {{globals.<id>.<value>}}. Lowercase letters, digits and dashes; can't change later."
        >
          <TextInput
            id="nvs-id"
            required
            value={slug}
            onChange={(e) => {
              setSlugTouched(true);
              setSlug(e.target.value.toLowerCase());
            }}
            placeholder="sap-prod"
            className="font-mono"
          />
        </Field>
        <FormError>{create.error?.message}</FormError>
        <div className="flex justify-end gap-2">
          <Button onClick={onClose}>Cancel</Button>
          <Button type="submit" variant="primary" busy={create.isPending}>
            Create value set
          </Button>
        </div>
      </form>
    </Dialog>
  );
}

/** ?subscriptions=3,5 — no id can be 0, so filter/join round-trip cleanly through this. */
const parseIds = (raw: string | null): number[] =>
  raw
    ? raw
        .split(",")
        .map(Number)
        .filter((n) => Number.isInteger(n) && n > 0)
    : [];

export function GlobalValueSetsPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const navigate = useNavigate();
  const q = searchParams.get("q") ?? "";
  const subscriptionIds = parseIds(searchParams.get("subscriptions"));
  const creating = searchParams.get("new") === "1";

  const sets = useQuery({ queryKey: keys.valueSets.list, queryFn: () => api.listValueSets() });
  const subscriptions = useSubscriptionsCache().data ?? [];

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
    const wantedSubscriptions = subscriptions.filter((i) => subscriptionIds.includes(i.id));
    return (sets.data ?? []).filter((s) => {
      if (needle && !s.name.toLowerCase().includes(needle) && !s.id.includes(needle)) return false;
      if (subscriptionIds.length > 0 && !wantedSubscriptions.some((i) => referencesGlobal(i, s.id))) return false;
      return true;
    });
  }, [sets.data, q, subscriptionIds, subscriptions]);

  return (
    <div>
      <PageHeader
        title="Global values"
        description="Shared value sets any adapter can reference — change an endpoint here once instead of in every subscription."
        actions={
          <Can permission="global-values.create">
            <Button variant="primary" onClick={() => setParam("new", "1")}>
              <Plus className="size-4" /> New value set
            </Button>
          </Can>
        }
      />

      <div className="mb-4 flex flex-wrap items-center gap-2">
        <div className="relative w-full max-w-xs">
          <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-ink-400" />
          <input
            type="search"
            value={q}
            onChange={(e) => setParam("q", e.target.value || null)}
            placeholder="Search value sets"
            aria-label="Search value sets"
            className="h-9 w-full rounded-lg border border-ink-200 bg-white pr-3 pl-9 text-sm placeholder:text-ink-400 focus:border-crimson-400 focus:ring-2 focus:ring-crimson-100 focus:outline-none"
          />
        </div>
        <div className="w-60">
          <SubscriptionMultiFilter
            subscriptions={subscriptions}
            selected={subscriptionIds}
            onChange={(ids) => setParam("subscriptions", ids.length ? ids.join(",") : null)}
            label="Filter by subscription"
          />
        </div>
      </div>

      {sets.isPending ? (
        <LoadingBlock label="Loading value sets…" />
      ) : filtered.length === 0 ? (
        <EmptyState
          icon={<SlidersHorizontal />}
          title={q || subscriptionIds.length > 0 ? "No value sets match" : "No value sets yet"}
        >
          {q || subscriptionIds.length > 0
            ? "Try a different search or filter."
            : "Create a set of shared values your adapters can reference."}
        </EmptyState>
      ) : (
        <Table
          rows={filtered}
          rowKey={(s) => s.id}
          minWidth="min-w-120"
          onRowClick={(s) => navigate(`/global-values/${s.id}`)}
          columns={[
            { header: "ID", cell: (s) => <code className="font-mono text-xs text-ink-700">{s.id}</code> },
            { header: "Name", cell: (s) => <span className="font-medium text-ink-900">{s.name}</span> },
            {
              header: "Values",
              align: "right",
              cell: (s) => <span className="tabular-nums text-ink-600">{Object.keys(s.values).length || "—"}</span>,
            },
            {
              header: "Used by",
              truncate: true,
              cell: (s) => <UsedByCell items={subscriptions.filter((i) => referencesGlobal(i, s.id))} />,
            },
          ]}
        />
      )}

      {creating && <CreateValueSetDialog onClose={() => setParam("new", null)} />}
    </div>
  );
}
