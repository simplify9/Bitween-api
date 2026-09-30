import { useEffect, useMemo, useState, type FormEvent } from "react";
import { Link, useNavigate, useParams } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { FlaskConical, Pencil, Plus, Trash2 } from "lucide-react";
import { api, type RetryGroup, type RetryMatcher, type RetryResultType } from "../../api";
import { Can, useSessionCan } from "../../auth/guards";
import { HistoryCard } from "../../components/config/HistoryCard";
import { Badge, Button, EmptyState, FormError, LoadingBlock } from "../../components/ui/basics";
import { Field, Select, TextInput } from "../../components/ui/forms";
import { ConfirmDialog } from "../../components/ui/overlays";
import { EditableTitle, Panel, UnsavedBar } from "../../components/ui/Panel";
import { MiniTable } from "../../components/ui/Table";
import { NotificationChannelPicker } from "../../components/config/pickers";
import { useNotificationChannelNames } from "../../lib/notificationChannels";
import { GroupDialog } from "./GroupDialog";
import { UsagePanel } from "./UsagePanel";
import { BackLink } from "../../components/ui/BackLink";
import { keys } from "../../api/queryKeys";

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
 * The policy-wide alert: which notification channel it goes through, staged into the page's save
 * bar like everything else. A group routes its own alert in the group dialog, and one
 * subscription-and-group pair in the override dialog.
 */
function PolicyAlertCard({
  channelId,
  groups,
  canEdit,
  onChange,
}: {
  channelId: number | null;
  groups: RetryGroup[];
  canEdit: boolean;
  onChange: (channelId: number | null) => void;
}) {
  const channels = useNotificationChannelNames();
  const channelName = channels.data?.find((c) => c.id === channelId)?.name;

  // Only a group that retries within a total can exhaust a budget, so only those can inherit an alert.
  const canAlert = groups.filter((g) => g.action === "Allow" && g.budget?.maxAttemptsTotal !== null);
  const inheriting = canAlert.filter((g) => g.alertMode === "Inherit");

  return (
    <Panel
      title="Budget-exhausted alert"
      description="Sent when a group stops retrying. Groups and single subscriptions can each route their own instead."
    >
      {canEdit ? (
        <NotificationChannelPicker value={channelId} onChange={onChange} clearLabel="No alert" />
      ) : (
        <p className="text-[13px] text-ink-800">{channelName ?? (channelId === null ? "No alert" : "…")}</p>
      )}
      <p className="mt-2 text-[13px] text-ink-500">
        {channelId === null
          ? "Nothing is sent when a budget runs out, unless a group or a single subscription routes one itself."
          : inheriting.length === 0
            ? "No group inherits it — each one routes its own alert, or is silent."
            : `${inheriting.length} of ${canAlert.length} ${canAlert.length === 1 ? "group sends" : "groups send"} here.`}
      </p>
    </Panel>
  );
}

export function RetryPolicyPage() {
  const { id = "" } = useParams();
  const policyId = Number(id);
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const canEdit = useSessionCan("retry-policies.edit");
  const channelNames = useNotificationChannelNames();

  const policy = useQuery({
    queryKey: keys.retryPolicies.detail(policyId),
    queryFn: () => api.getRetryPolicy(policyId),
    retry: false,
  });

  const [name, setName] = useState("");
  const [groups, setGroups] = useState<RetryGroup[] | null>(null);
  const [alertChannelId, setAlertChannelId] = useState<number | null>(null);
  const [editingGroup, setEditingGroup] = useState<RetryGroup | "new" | null>(null);
  const [deleting, setDeleting] = useState(false);
  const [loaded, setLoaded] = useState(false);

  useEffect(() => {
    if (!loaded && policy.data) {
      setName(policy.data.name);
      setGroups(structuredClone(policy.data.groups));
      setAlertChannelId(policy.data.alertChannelId);
      setLoaded(true);
    }
  }, [policy.data, loaded]);

  const dirty = useMemo(() => {
    if (!policy.data || groups === null) return false;
    return (
      name !== policy.data.name ||
      JSON.stringify(groups) !== JSON.stringify(policy.data.groups) ||
      alertChannelId !== policy.data.alertChannelId
    );
  }, [policy.data, name, groups, alertChannelId]);

  const save = useMutation({
    mutationFn: () =>
      api.updateRetryPolicy(policyId, {
        name,
        groups: groups ?? [],
        alertChannelId,
      }),
    onSuccess: async () => {
      // Awaited before the draft is re-synced, or the re-sync would seed from stale data.
      await queryClient.invalidateQueries({ queryKey: keys.retryPolicies.all });
      // Editing a group can change which budgets exist, so the usage report is stale too.
      void queryClient.invalidateQueries({ queryKey: keys.retryUsage.all });
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
      <BackLink to="/retry-policies" label="Retry policies" />

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
                  wrap: true,
                  cell: (g) => (
                    <span
                      title={g.notes ? `${g.name} — ${g.notes}` : g.name}
                      className={`block font-medium text-ink-900 ${g.enabled ? "" : "opacity-60"}`}
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
                  wrap: true,
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
                        className="block text-[13px] text-ink-600"
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
                        {g.budget.maxAttemptsPerError} tries (
                        {g.budget.maxAttemptsTotal == null ? "no total limit" : `${g.budget.maxAttemptsTotal} total`}) ·{" "}
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
                    ) : g.budget && g.budget.maxAttemptsTotal === null ? (
                      <span className="text-ink-400" title="No total limit, so the budget never runs out and there is nothing to alert on.">
                        —
                      </span>
                    ) : g.alertMode === "Silent" ? (
                      <span className="text-[13px] text-ink-500">Silent</span>
                    ) : g.alertMode === "Send" && g.alertChannelId !== null ? (
                      <span className="block truncate text-[13px] text-ink-700">
                        {channelNames.data?.find((c) => c.id === g.alertChannelId)?.name ?? "…"}
                      </span>
                    ) : (
                      <span className="text-[13px] text-ink-400 italic">
                        {alertChannelId !== null ? "Inherited" : "Nobody"}
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
            channelId={alertChannelId}
            groups={sortedGroups}
            canEdit={canEdit}
            onChange={setAlertChannelId}
          />
        </div>
      </div>

      <div className="mt-5 space-y-5">
        <UsagePanel policyId={policyId} subscriptions={p.subscriptions} canEdit={canEdit} />
        <TestPanel groups={groups ?? []} />
        <HistoryCard entityName="RetryPolicy" entityKey={policyId} />
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
          policyAlertChannelId={alertChannelId}
        />
      )}

      {deleting && (
        <ConfirmDialog
          title="Delete this retry policy?"
          body={
            <>
              <strong className="font-medium text-ink-800">{p.name}</strong> will be gone for good.
              Policies still assigned to subscriptions can't be deleted.
            </>
          }
          confirmLabel="Delete policy"
          onConfirm={async () => {
            await api.deleteRetryPolicy(policyId);
            void queryClient.invalidateQueries({ queryKey: keys.retryPolicies.all });
            navigate("/retry-policies", { replace: true });
          }}
          onClose={() => setDeleting(false)}
        />
      )}
    </div>
  );
}
