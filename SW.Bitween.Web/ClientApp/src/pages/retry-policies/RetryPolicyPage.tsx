import { useEffect, useMemo, useState, type FormEvent } from "react";
import { Link, useNavigate, useParams } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, FlaskConical, Pencil, Plus, Trash2 } from "lucide-react";
import { api, type RetryGroup, type RetryMatcher, type RetryResultType } from "../../api";
import { Can, useSessionCan } from "../../auth/guards";
import { Badge, Button, EmptyState, FormError, LoadingBlock } from "../../components/ui/basics";
import { Field, Select, TextInput } from "../../components/ui/forms";
import { ConfirmDialog } from "../../components/ui/overlays";
import { EditableTitle, Panel, UnsavedBar } from "../../components/ui/Panel";
import { SetupList } from "../../components/config/shared";
import { GroupDialog } from "./GroupDialog";

const matcherSummary = (m: RetryMatcher): string => {
  switch (m.type) {
    case "contains":
      return `contains "${m.value}"`;
    case "regex":
      return `matches /${m.pattern}/${m.flags}`;
    case "exceptionType":
      return `exception ${m.value}`;
    case "jsonPath":
      return `${m.path} ${m.op.toLowerCase()}${m.value ? ` "${m.value}"` : ""}`;
  }
};

/** Dry-run the draft groups against a sample failure, right on the page. */
function TestPanel({ groups }: { groups: RetryGroup[] }) {
  const [resultType, setResultType] = useState<RetryResultType>("Error");
  const [attempts, setAttempts] = useState(5);
  const [content, setContent] = useState("");

  const test = useMutation({
    mutationFn: () => api.testRetryPolicy({ groups, resultType, content, attempts }),
  });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    test.mutate();
  };

  return (
    <Panel
      title="Try it out"
      description="Simulate a failure against the groups as configured above — including unsaved changes."
    >
      <form onSubmit={submit} className="space-y-3">
        <div className="grid gap-3 sm:grid-cols-2">
          <Field label="Failure kind" htmlFor="tp-kind">
            <Select
              id="tp-kind"
              value={resultType}
              onChange={(e) => setResultType(e.target.value as RetryResultType)}
              options={[
                { value: "Error", label: "Error (exception)" },
                { value: "BadResult", label: "Bad result (reply says failed)" },
              ]}
            />
          </Field>
          <Field label="Attempts to simulate" htmlFor="tp-attempts">
            <TextInput
              id="tp-attempts"
              type="number"
              min={1}
              max={20}
              value={attempts}
              onChange={(e) => setAttempts(Number(e.target.value))}
            />
          </Field>
        </div>
        <Field
          label={resultType === "Error" ? "Error text" : "Result body (JSON)"}
          htmlFor="tp-content"
        >
          <textarea
            id="tp-content"
            rows={3}
            value={content}
            onChange={(e) => setContent(e.target.value)}
            placeholder={
              resultType === "Error"
                ? "e.g. HttpRequestException: The request timed out"
                : 'e.g. { "status": "REJECTED", "reason": "…" }'
            }
            className="w-full rounded-lg border border-ink-200 bg-white px-3 py-2 font-mono text-xs text-ink-900 placeholder:text-ink-400 focus:border-crimson-400 focus:ring-2 focus:ring-crimson-100 focus:outline-none"
          />
        </Field>
        <FormError>{test.error?.message}</FormError>
        <Button type="submit" variant="primary" size="sm" busy={test.isPending}>
          <FlaskConical className="size-3.5" /> Run simulation
        </Button>
      </form>

      {test.data && (
        <ol className="mt-4 space-y-1.5 border-t border-ink-100 pt-3">
          {test.data.map((a) => (
            <li key={a.attempt} className="flex items-start gap-2.5 text-[13px]">
              <span className="mt-0.5 w-14 shrink-0 font-mono text-xs text-ink-400">#{a.attempt}</span>
              {a.shouldRetry ? <Badge tone="ok">Retries</Badge> : <Badge tone="danger">Stops</Badge>}
              <span className="min-w-0 flex-1 text-ink-600">
                {a.matchedGroup && <strong className="font-medium text-ink-800">{a.matchedGroup}: </strong>}
                {a.reason}
                {a.delaySeconds !== undefined && ` Next try in ${a.delaySeconds}s.`}
              </span>
            </li>
          ))}
        </ol>
      )}
    </Panel>
  );
}

export function RetryPolicyPage() {
  const { id = "" } = useParams();
  const policyId = Number(id);
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const canEdit = useSessionCan("retry-policies.edit");

  const policy = useQuery({
    queryKey: ["retry-policy", policyId],
    queryFn: () => api.getRetryPolicy(policyId),
    retry: false,
  });

  const [name, setName] = useState("");
  const [groups, setGroups] = useState<RetryGroup[] | null>(null);
  const [editingGroup, setEditingGroup] = useState<RetryGroup | "new" | null>(null);
  const [deleting, setDeleting] = useState(false);
  const [loaded, setLoaded] = useState(false);

  useEffect(() => {
    if (!loaded && policy.data) {
      setName(policy.data.name);
      setGroups(structuredClone(policy.data.groups));
      setLoaded(true);
    }
  }, [policy.data, loaded]);

  const dirty = useMemo(() => {
    if (!policy.data || groups === null) return false;
    return name !== policy.data.name || JSON.stringify(groups) !== JSON.stringify(policy.data.groups);
  }, [policy.data, name, groups]);

  const save = useMutation({
    mutationFn: () => api.updateRetryPolicy(policyId, { name, groups: groups ?? [] }),
    onSuccess: async () => {
      // Await the detail refetch before re-syncing the draft (avoids stale-data race).
      await queryClient.invalidateQueries({ queryKey: ["retry-policy", policyId] });
      void queryClient.invalidateQueries({ queryKey: ["retry-policies"] });
      setLoaded(false);
    },
  });

  if (policy.isPending) return <LoadingBlock label="Loading retry policy…" />;
  if (policy.isError)
    return (
      <EmptyState title="This retry policy no longer exists">
        <Link to="/retry-policies" className="font-medium text-crimson-700 hover:underline">
          Back to retry policies
        </Link>
      </EmptyState>
    );

  const p = policy.data;
  const sortedGroups = [...(groups ?? [])].sort((a, b) => a.priority - b.priority);

  const upsertGroup = (group: RetryGroup) =>
    setGroups((prev) => {
      const list = prev ?? [];
      return list.some((g) => g.id === group.id)
        ? list.map((g) => (g.id === group.id ? group : g))
        : [...list, group];
    });

  return (
    <div className="pb-24">
      <Link
        to="/retry-policies"
        className="mb-4 inline-flex items-center gap-1 text-[13px] font-medium text-ink-500 hover:text-ink-800"
      >
        <ArrowLeft className="size-3.5" /> Retry policies
      </Link>

      <div className="mb-6 flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-[22px] font-semibold tracking-tight text-ink-900">
            <EditableTitle value={name} onChange={setName} disabled={!canEdit} placeholder="Policy name" />
          </h1>
          <p className="mt-1 text-sm text-ink-500">
            Groups are checked top to bottom — the first match decides.
          </p>
        </div>
        <Can permission="retry-policies.delete">
          <Button variant="danger" onClick={() => setDeleting(true)}>
            <Trash2 className="size-4" /> Delete
          </Button>
        </Can>
      </div>

      <div className="grid gap-5 lg:grid-cols-[minmax(0,1fr)_360px]">
        <div className="min-w-0 space-y-5">
          <Panel
            title="Groups"
            action={
              canEdit ? (
                <Button size="sm" onClick={() => setEditingGroup("new")}>
                  <Plus className="size-3.5" /> Add group
                </Button>
              ) : undefined
            }
          >
            {sortedGroups.length === 0 ? (
              <p className="text-sm text-ink-500">
                No groups yet — failures under this policy are never retried.
              </p>
            ) : (
              <ul className="space-y-2">
                {sortedGroups.map((g) => (
                  <li key={g.id} className={`rounded-xl border border-ink-200 p-3.5 ${g.enabled ? "" : "opacity-60"}`}>
                    <div className="flex flex-wrap items-center gap-2">
                      <span className="font-mono text-xs text-ink-400">#{g.priority}</span>
                      <span className="font-medium text-ink-900">{g.name}</span>
                      {g.action === "Allow" ? <Badge tone="ok">Retries</Badge> : <Badge tone="danger">Blocks</Badge>}
                      {!g.enabled && <Badge>Disabled</Badge>}
                      <span className="ml-auto flex gap-1">
                        {canEdit && (
                          <>
                            <button
                              onClick={() => setEditingGroup(g)}
                              aria-label={`Edit ${g.name}`}
                              className="rounded-md p-1.5 text-ink-400 hover:bg-ink-100 hover:text-ink-700"
                            >
                              <Pencil className="size-3.5" />
                            </button>
                            <button
                              onClick={() => setGroups((prev) => (prev ?? []).filter((x) => x.id !== g.id))}
                              aria-label={`Remove ${g.name}`}
                              className="rounded-md p-1.5 text-ink-400 hover:bg-danger-50 hover:text-danger-700"
                            >
                              <Trash2 className="size-3.5" />
                            </button>
                          </>
                        )}
                      </span>
                    </div>
                    <p className="mt-1.5 text-[13px] text-ink-600">
                      On {g.appliesTo.map((t) => (t === "Error" ? "errors" : "bad results")).join(" and ")}
                      {g.matchers.length === 0 ? (
                        " — any failure"
                      ) : (
                        <> matching {g.matchers.map((m) => matcherSummary(m)).join(" or ")}</>
                      )}
                      {g.action === "Allow" && g.budget && (
                        <>
                          {" "}
                          → up to {g.budget.maxAttemptsPerError} tries ({g.budget.maxAttemptsTotal} total),{" "}
                          {g.budget.delay.type} delay
                        </>
                      )}
                    </p>
                    {g.notes && <p className="mt-1 text-xs text-ink-400 italic">{g.notes}</p>}
                  </li>
                ))}
              </ul>
            )}
          </Panel>

          <TestPanel groups={groups ?? []} />
        </div>

        <div className="min-w-0 space-y-5">
          <Panel title="Used by" description="Integrations that follow this policy after failures.">
            <SetupList items={p.integrations} />
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

      {editingGroup && (
        <GroupDialog
          initial={editingGroup === "new" ? undefined : editingGroup}
          onSubmit={upsertGroup}
          onClose={() => setEditingGroup(null)}
        />
      )}

      {deleting && (
        <ConfirmDialog
          title="Delete this retry policy?"
          body={
            <>
              <strong className="font-medium text-ink-800">{p.name}</strong> will be gone for good.
              Policies still assigned to integrations can't be deleted.
            </>
          }
          confirmLabel="Delete policy"
          onConfirm={async () => {
            await api.deleteRetryPolicy(policyId);
            void queryClient.invalidateQueries({ queryKey: ["retry-policies"] });
            navigate("/retry-policies");
          }}
          onClose={() => setDeleting(false)}
        />
      )}
    </div>
  );
}
