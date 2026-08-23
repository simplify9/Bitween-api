import { useEffect, useMemo, useState, type FormEvent } from "react";
import { Link, useNavigate, useParams } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, FlaskConical, Pencil, Plus, Trash2 } from "lucide-react";
import { api, type RetryGroup, type RetryMatcher, type RetryResultType } from "../../api";
import { Can, useSessionCan } from "../../auth/guards";
import { Badge, Button, EmptyState, FormError, LoadingBlock } from "../../components/ui/basics";
import { Field, Select, TextInput } from "../../components/ui/forms";
import { ConfirmDialog, Dialog } from "../../components/ui/overlays";
import { EditableTitle, Panel, UnsavedBar } from "../../components/ui/Panel";
import { MiniTable } from "../../components/ui/Table";
import { AdapterConfig } from "../../components/config/AdapterConfig";
import { GroupDialog } from "./GroupDialog";
import { UsagePanel } from "./UsagePanel";

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

/**
 * The policy-wide alert, summarised — with its adapter form behind a dialog.
 *
 * Left open, a mail handler's thirteen fields filled the whole column and left the groups
 * table sitting beside a void; two columns of them inside a 360px rail wrapped every address
 * onto three lines. It is also set once and rarely revisited, where everything around it is
 * read on every visit, so it had the run of the page on the strength of being the longest
 * form rather than the most useful one.
 *
 * A dialog makes the three levels consistent too: a group routes its own alert in the group
 * dialog, one integration-and-group pair in the override dialog, and the policy default here.
 * All three stage into the same save bar.
 */
function PolicyAlertCard({
  handlerId,
  properties,
  groups,
  canEdit,
  onChange,
}: {
  handlerId: string | null;
  properties: Record<string, string>;
  groups: RetryGroup[];
  canEdit: boolean;
  onChange: (handlerId: string | null, properties: Record<string, string>) => void;
}) {
  const [editing, setEditing] = useState(false);
  const [draftId, setDraftId] = useState(handlerId);
  const [draftProps, setDraftProps] = useState(properties);

  // Only a group that retries can exhaust a budget, so only those can inherit an alert.
  const canAlert = groups.filter((g) => g.action === "Allow");
  const inheriting = canAlert.filter((g) => g.alertMode === "Inherit");

  const open = () => {
    setDraftId(handlerId);
    setDraftProps(properties);
    setEditing(true);
  };

  return (
    <Panel
      title="Budget-exhausted alert"
      description="Sent when a group stops retrying. Groups and single integrations can each route their own instead."
      action={
        canEdit ? (
          <Button size="sm" onClick={open}>
            {handlerId ? "Change" : "Set up"}
          </Button>
        ) : undefined
      }
    >
      {handlerId ? (
        <>
          <p className="font-mono text-[13px] text-ink-800">{handlerId}</p>
          <p className="mt-1 text-[13px] text-ink-500">
            {inheriting.length === 0
              ? "No group inherits it — each one routes its own alert, or is silent."
              : `${inheriting.length} of ${canAlert.length} ${canAlert.length === 1 ? "group sends" : "groups send"} here.`}
          </p>
        </>
      ) : (
        <p className="text-[13px] text-ink-500">
          No alert. Nothing is sent when a budget runs out, unless a group or a single
          integration routes one itself.
        </p>
      )}

      {editing && (
        <Dialog title="Budget-exhausted alert" onClose={() => setEditing(false)} wide>
          <div className="space-y-4">
            <p className="text-[13px] text-ink-500">
              Where this policy sends an alert when any of its groups stops retrying. Saved with
              the rest of the page.
            </p>
            <AdapterConfig
              kind="handler"
              adapterId={draftId}
              properties={draftProps}
              disabled={false}
              noneLabel="No alert — nothing is sent when a budget runs out"
              onChange={(id, props) => {
                setDraftId(id);
                setDraftProps(props);
              }}
            />
            <div className="flex justify-end gap-2">
              <Button onClick={() => setEditing(false)}>Cancel</Button>
              <Button
                variant="primary"
                onClick={() => {
                  onChange(draftId, draftProps);
                  setEditing(false);
                }}
              >
                Done
              </Button>
            </div>
          </div>
        </Dialog>
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
  const [alertHandlerId, setAlertHandlerId] = useState<string | null>(null);
  const [alertProps, setAlertProps] = useState<Record<string, string>>({});
  const [editingGroup, setEditingGroup] = useState<RetryGroup | "new" | null>(null);
  const [deleting, setDeleting] = useState(false);
  const [loaded, setLoaded] = useState(false);

  useEffect(() => {
    if (!loaded && policy.data) {
      setName(policy.data.name);
      setGroups(structuredClone(policy.data.groups));
      setAlertHandlerId(policy.data.alertHandlerId);
      setAlertProps(structuredClone(policy.data.alertHandlerProperties));
      setLoaded(true);
    }
  }, [policy.data, loaded]);

  const dirty = useMemo(() => {
    if (!policy.data || groups === null) return false;
    return (
      name !== policy.data.name ||
      JSON.stringify(groups) !== JSON.stringify(policy.data.groups) ||
      alertHandlerId !== policy.data.alertHandlerId ||
      JSON.stringify(alertProps) !== JSON.stringify(policy.data.alertHandlerProperties)
    );
  }, [policy.data, name, groups, alertHandlerId, alertProps]);

  const save = useMutation({
    mutationFn: () =>
      api.updateRetryPolicy(policyId, {
        name,
        groups: groups ?? [],
        alertHandlerId,
        alertHandlerProperties: alertProps,
      }),
    onSuccess: async () => {
      // Await the detail refetch before re-syncing the draft (avoids stale-data race).
      await queryClient.invalidateQueries({ queryKey: ["retry-policy", policyId] });
      void queryClient.invalidateQueries({ queryKey: ["retry-policies"] });
      // Editing a group can change which budgets exist, so the usage report is stale too.
      void queryClient.invalidateQueries({ queryKey: ["retry-usage"] });
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
            <MiniTable
              rows={sortedGroups}
              rowKey={(g) => g.id}
              fitWidth
              onRowClick={canEdit ? (g) => setEditingGroup(g) : undefined}
              empty="No groups yet — failures under this policy are never retried."
              columns={[
                {
                  header: "#",
                  className: "w-8",
                  cell: (g) => <span className="font-mono text-xs text-ink-400">{g.priority}</span>,
                },
                {
                  header: "Group",
                  truncate: true,
                  cell: (g) => (
                    <span
                      title={g.notes ? `${g.name} — ${g.notes}` : g.name}
                      className={`block truncate font-medium text-ink-900 ${g.enabled ? "" : "opacity-60"}`}
                    >
                      {g.name}
                    </span>
                  ),
                },
                {
                  header: "Action",
                  cell: (g) => (
                    <span className="flex flex-wrap gap-1">
                      {g.action === "Allow" ? <Badge tone="ok">Retries</Badge> : <Badge tone="danger">Blocks</Badge>}
                      {!g.enabled && <Badge>Disabled</Badge>}
                    </span>
                  ),
                },
                {
                  header: "Applies to",
                  truncate: true,
                  cell: (g) => {
                    // Scope first and short, conditions second: every row in a policy tends to
                    // share the scope, so leading with "errors matching " spent the column's
                    // width on the one part that never tells them apart.
                    const scope =
                      g.appliesTo.length === 1 && g.appliesTo[0] === "Error"
                        ? null
                        : g.appliesTo.map((t) => (t === "Error" ? "Errors" : "Bad results")).join(" + ");
                    const conditions =
                      g.matchers.length === 0
                        ? "any failure"
                        : g.matchers.map((m) => matcherSummary(m)).join(" or ");
                    return (
                      <span
                        className="block truncate text-[13px] text-ink-600"
                        title={scope ? `${scope} · ${conditions}` : conditions}
                      >
                        {scope && <span className="text-ink-400">{scope} · </span>}
                        {conditions}
                      </span>
                    );
                  },
                },
                {
                  // Bounded text — two numbers and one of three delay names — so it shrinks to
                  // fit instead of truncating, leaving the slack to the columns that need it.
                  header: "Budget",
                  cell: (g) =>
                    g.action === "Allow" && g.budget ? (
                      <span className="text-[13px] text-ink-600">
                        {g.budget.maxAttemptsPerError} tries ({g.budget.maxAttemptsTotal} total) ·{" "}
                        {g.budget.delay.type}
                      </span>
                    ) : (
                      <span className="text-ink-400">—</span>
                    ),
                },
                {
                  header: "Alert",
                  truncate: true,
                  cell: (g) =>
                    g.action !== "Allow" ? (
                      <span className="text-ink-400">—</span>
                    ) : g.alertMode === "Silent" ? (
                      <span className="text-[13px] text-ink-500">Silent</span>
                    ) : g.alertMode === "Send" && g.alertHandlerId ? (
                      <span className="block truncate font-mono text-xs text-ink-700">{g.alertHandlerId}</span>
                    ) : (
                      <span className="text-[13px] text-ink-400 italic">
                        {alertHandlerId ? "Inherited" : "Nobody"}
                      </span>
                    ),
                },
                {
                  header: "",
                  align: "right",
                  cell: (g) =>
                    canEdit ? (
                      <span className="flex justify-end gap-1" onClick={(e) => e.stopPropagation()}>
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
                      </span>
                    ) : null,
                },
              ]}
            />
          </Panel>

        </div>

        <div className="min-w-0 space-y-5">
          <PolicyAlertCard
            handlerId={alertHandlerId}
            properties={alertProps}
            groups={sortedGroups}
            canEdit={canEdit}
            onChange={(id, props) => {
              setAlertHandlerId(id);
              setAlertProps(props);
            }}
          />
        </div>
      </div>

      <div className="mt-5 space-y-5">
        <UsagePanel policyId={policyId} integrations={p.integrations} canEdit={canEdit} />
        <TestPanel groups={groups ?? []} />
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
          policyAlertHandlerId={alertHandlerId}
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
