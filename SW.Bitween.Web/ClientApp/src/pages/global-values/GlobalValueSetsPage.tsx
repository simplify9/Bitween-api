import { useMemo, useState, type FormEvent } from "react";
import { useNavigate, useSearchParams } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Plus, Search, SlidersHorizontal } from "lucide-react";
import { api } from "../../api";
import { Can } from "../../auth/guards";
import { PageHeader } from "../../components/layout/PageHeader";
import { Button, EmptyState, FormError, LoadingBlock } from "../../components/ui/basics";
import { Field, TextInput } from "../../components/ui/forms";
import { Dialog } from "../../components/ui/overlays";
import { suggestSlug } from "../../lib/identifiers";

function CreateValueSetDialog({ onClose }: { onClose: () => void }) {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [name, setName] = useState("");
  const [slug, setSlug] = useState("");
  const [slugTouched, setSlugTouched] = useState(false);

  const create = useMutation({
    mutationFn: () => api.createValueSet({ id: slug, name, values: {} }),
    onSuccess: (set) => {
      void queryClient.invalidateQueries({ queryKey: ["value-sets"] });
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

export function GlobalValueSetsPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const navigate = useNavigate();
  const q = searchParams.get("q") ?? "";
  const creating = searchParams.get("new") === "1";

  const sets = useQuery({ queryKey: ["value-sets"], queryFn: () => api.listValueSets() });

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
    return (sets.data ?? []).filter(
      (s) => !needle || s.name.toLowerCase().includes(needle) || s.id.includes(needle),
    );
  }, [sets.data, q]);

  return (
    <div>
      <PageHeader
        title="Global values"
        description="Shared value sets any adapter can reference — change an endpoint here once instead of in every integration."
        actions={
          <Can permission="global-values.create">
            <Button variant="primary" onClick={() => setParam("new", "1")}>
              <Plus className="size-4" /> New value set
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
          placeholder="Search value sets"
          aria-label="Search value sets"
          className="h-9 w-full rounded-lg border border-ink-200 bg-white pr-3 pl-9 text-sm placeholder:text-ink-400 focus:border-crimson-400 focus:ring-2 focus:ring-crimson-100 focus:outline-none"
        />
      </div>

      {sets.isPending ? (
        <LoadingBlock label="Loading value sets…" />
      ) : filtered.length === 0 ? (
        <EmptyState icon={<SlidersHorizontal />} title={q ? "No value sets match" : "No value sets yet"}>
          {q ? "Try a different search." : "Create a set of shared values your adapters can reference."}
        </EmptyState>
      ) : (
        <div className="overflow-x-auto rounded-xl border border-ink-200 bg-white">
          <table className="w-full min-w-120 text-sm">
            <thead>
              <tr className="border-b border-ink-100 text-left text-xs text-ink-500">
                <th className="px-4 py-2.5 font-medium">ID</th>
                <th className="px-4 py-2.5 font-medium">Name</th>
                <th className="px-4 py-2.5 font-medium">Values</th>
                <th className="px-4 py-2.5 font-medium">Used by</th>
              </tr>
            </thead>
            <tbody>
              {filtered.map((s) => (
                <tr
                  key={s.id}
                  onClick={() => navigate(`/global-values/${s.id}`)}
                  className="cursor-pointer border-b border-ink-100 last:border-b-0 hover:bg-ink-50"
                >
                  <td className="px-4 py-3">
                    <code className="font-mono text-xs text-ink-700">{s.id}</code>
                  </td>
                  <td className="px-4 py-3 font-medium text-ink-900">{s.name}</td>
                  <td className="px-4 py-3 text-ink-600">{Object.keys(s.values).length}</td>
                  <td className="px-4 py-3 text-ink-600">
                    {s.usedByCount > 0 ? (
                      `${s.usedByCount} integration${s.usedByCount === 1 ? "" : "s"}`
                    ) : (
                      <span className="text-ink-400">—</span>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {creating && <CreateValueSetDialog onClose={() => setParam("new", null)} />}
    </div>
  );
}
