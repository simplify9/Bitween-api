import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { api, type SubscriptionRevision } from "../../../api";
import { keys } from "../../../api/queryKeys";
import { Can } from "../../../auth/guards";
import { Button, LoadingBlock } from "../../../components/ui/basics";
import { ConfirmDialog, Dialog } from "../../../components/ui/overlays";
import { Panel } from "../../../components/ui/Panel";
import { MiniTable } from "../../../components/ui/Table";
import { formatDateTime } from "../../../lib/dates";

/** What the API calls a setting, as a person reads it. */
const SETTING_NAMES: Record<string, string> = {
  name: "Name",
  handlerId: "Delivery adapter",
  handlerVersion: "Delivery adapter version",
  handlerProperties: "Delivery settings",
  mapperId: "Mapper",
  mapperVersion: "Mapper version",
  mapperProperties: "Mapping",
  receiverId: "Receiver",
  receiverVersion: "Receiver version",
  receiverProperties: "Receiver settings",
  validatorId: "Validator",
  validatorVersion: "Validator version",
  validatorProperties: "Validator settings",
  documentFilter: "Filter",
  matchExpression: "Match expression",
  schedules: "Schedule",
  responseSubscriptionId: "Response subscription",
  responseMessageTypeName: "Response message name",
  runOnBadResponses: "Run on bad responses",
  retryPolicyId: "Retry policy",
  customRetryPolicy: "Retry policy",
  autoPauseAfterFailures: "Auto-pause",
  expectTraffic: "Expected traffic",
  workGroupId: "Work group",
  categoryId: "Category",
  dataSourceId: "Data source",
  aggregationTarget: "Aggregation target",
};

const settingName = (key: string) => SETTING_NAMES[key] ?? key;

/**
 * Every save that changed this subscription's configuration, newest first. A version can be
 * compared with what the subscription has now, and restored: saved again as it was, which is
 * checked like any save and becomes a version of its own. Secrets are never kept in a version,
 * so restoring keeps the ones the subscription has now - unless the version points at another
 * destination, in which case they have to be entered again, as with any such save.
 */
export function VersionsPanel({ subscriptionId }: { subscriptionId: number }) {
  const queryClient = useQueryClient();
  const revisions = useQuery({
    queryKey: keys.subscriptions.revisions(subscriptionId),
    queryFn: () => api.listSubscriptionRevisions(subscriptionId),
  });
  const [comparing, setComparing] = useState<number | null>(null);
  const [restoring, setRestoring] = useState<SubscriptionRevision | null>(null);
  const newest = revisions.data?.[0]?.number;

  return (
    <Panel
      title="Versions"
      description="Each save that changed this subscription's configuration. Kept from the first save after versions existed, 50 at most."
    >
      {revisions.isLoading ? (
        <LoadingBlock />
      ) : (
        <MiniTable
          rows={revisions.data ?? []}
          rowKey={(r) => r.number}
          empty="No versions yet: the next save records one."
          columns={[
            { header: "Version", cell: (r) => <span className="font-medium text-ink-800">#{r.number}</span> },
            {
              header: "Saved",
              wrap: true,
              cell: (r) => (
                <span className="text-[13px] text-ink-600">
                  {formatDateTime(r.savedOn)}
                  {r.savedBy && <span className="text-ink-500"> by {r.savedBy}</span>}
                </span>
              ),
            },
            {
              header: "Changed",
              wrap: true,
              cell: (r) => (
                <span className="text-[13px] text-ink-600">
                  {r.number === revisions.data?.[revisions.data.length - 1]?.number && r.changed.length === 0
                    ? "First version recorded"
                    : r.changed.map(settingName).filter((n, i, all) => all.indexOf(n) === i).join(", ") || "-"}
                </span>
              ),
            },
            {
              header: "",
              align: "right",
              cell: (r) =>
                r.number === newest ? (
                  <span className="text-xs text-ink-500">Current</span>
                ) : (
                  <div className="flex justify-end gap-1.5">
                    <Button size="sm" variant="ghost" onClick={() => setComparing(r.number)}>
                      Compare
                    </Button>
                    <Can permission="subscriptions.edit">
                      <Button size="sm" onClick={() => setRestoring(r)}>
                        Restore
                      </Button>
                    </Can>
                  </div>
                ),
            },
          ]}
        />
      )}

      {comparing !== null && (
        <CompareDialog subscriptionId={subscriptionId} number={comparing} onClose={() => setComparing(null)} />
      )}

      {restoring && (
        <ConfirmDialog
          title={`Restore version #${restoring.number}?`}
          confirmVariant="primary"
          confirmLabel="Restore"
          body={
            <p className="text-sm text-ink-600">
              The subscription is saved with version #{restoring.number}'s configuration, checked like any save, and
              recorded as a new version. Whether it is active doesn't change, and secrets are kept, unless the version
              points at another destination: then they have to be entered again.
            </p>
          }
          onConfirm={async () => {
            await api.restoreSubscriptionRevision(subscriptionId, restoring.number);
            await queryClient.invalidateQueries({ queryKey: keys.subscriptions.all });
          }}
          onClose={() => setRestoring(null)}
        />
      )}
    </Panel>
  );
}

function CompareDialog({ subscriptionId, number, onClose }: { subscriptionId: number; number: number; onClose: () => void }) {
  const data = useQuery({
    queryKey: ["subscriptions", "revision-compare", subscriptionId, number],
    queryFn: async () => {
      const [revision, now] = await Promise.all([
        api.getSubscriptionRevision(subscriptionId, number),
        api.getSubscriptionConfiguration(subscriptionId),
      ]);
      return { revision, now };
    },
  });

  const differences = data.data
    ? Object.keys(data.data.revision.configuration)
        .filter((key) => JSON.stringify(data.data!.revision.configuration[key] ?? null) !== JSON.stringify(data.data!.now[key] ?? null))
        .map((key) => ({ key, then: data.data!.revision.configuration[key], now: data.data!.now[key] }))
    : [];

  return (
    <Dialog title={`Version #${number} compared with now`} onClose={onClose} wide>
      {data.isLoading ? (
        <LoadingBlock />
      ) : differences.length === 0 ? (
        <p className="text-sm text-ink-600">The configuration is the same as it is now.</p>
      ) : (
        <div className="space-y-4">
          {differences.map((d) => (
            <div key={d.key}>
              <h3 className="mb-1.5 text-xs font-semibold uppercase tracking-wide text-ink-500">{settingName(d.key)}</h3>
              <div className="grid gap-2 sm:grid-cols-2">
                <Value label={`Version #${number}`} value={d.then} />
                <Value label="Now" value={d.now} />
              </div>
            </div>
          ))}
          <p className="text-xs text-ink-500">Secrets show as __private__: versions never keep them.</p>
        </div>
      )}
    </Dialog>
  );
}

function Value({ label, value }: { label: string; value: unknown }) {
  const text =
    value === null || value === undefined || value === ""
      ? "(none)"
      : Array.isArray(value) && value.every((v) => v && typeof v === "object" && "key" in v)
        ? (value as { key: string; value: unknown }[]).map((v) => `${v.key}: ${v.value ?? ""}`).join("\n") || "(none)"
        : typeof value === "object"
          ? JSON.stringify(value, null, 2)
          : String(value);
  return (
    <div className="min-w-0 rounded-lg border border-ink-200 bg-ink-50 px-3 py-2">
      <p className="mb-1 text-[11px] font-medium text-ink-500">{label}</p>
      <pre className="max-h-64 overflow-auto whitespace-pre-wrap break-words font-mono text-xs text-ink-800">{text}</pre>
    </div>
  );
}
