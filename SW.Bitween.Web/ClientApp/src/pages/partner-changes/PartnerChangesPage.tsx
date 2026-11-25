import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { api, type PartnerChange } from "../../api";
import { keys } from "../../api/queryKeys";
import { PageHeader } from "../../components/layout/PageHeader";
import { EmptyState, LoadError, LoadingBlock } from "../../components/ui/basics";
import { SegmentedControl } from "../../components/ui/SegmentedControl";
import { Table } from "../../components/ui/Table";
import { PartnerChangeBadge, PartnerChangeDialog } from "../../components/partnerChanges/PartnerChangeDialog";
import { PartnerPropertyChangeDialog } from "../../components/partnerChanges/PartnerPropertyChangeDialog";
import { formatDateTime, timeAgo } from "../../lib/dates";

type Showing = "waiting" | "decided";

/**
 * What partners changed in the portal and wait for our approval to use, for partners whose changes
 * need it; and the latest decided. Opening one shows it beside what the partner gets now.
 */
export function PartnerChangesPage() {
  const [showing, setShowing] = useState<Showing>("waiting");
  const [opened, setOpened] = useState<PartnerChange | null>(null);
  const waiting = showing === "waiting";
  const changes = useQuery({ queryKey: keys.portal.changes(waiting), queryFn: () => api.listPartnerChanges(waiting) });

  const what = (c: PartnerChange) =>
    c.kind === "properties" ? "Their details" : c.backToOurs ? "Back to the subscription's mapping" : "Their own mapping";

  return (
    <div>
      <PageHeader
        title="Partner changes"
        description="Changes partners make in the portal, to their mappings or their details, waiting for your approval before they're used."
        help={{
          title: "How approval works",
          body: (
            <>
              <p>
                On a partner's page, <strong>Their changes need our approval</strong> decides whether what their people
                change in the portal is used at once or waits here.
              </p>
              <p>
                Approving uses the change from their next exchange. Rejecting keeps what they have now, and they see your
                reason. Every change, approved or not, is kept in the history on the subscription's page.
              </p>
            </>
          ),
        }}
      />

      <SegmentedControl
        className="mb-4"
        label="Which changes"
        value={showing}
        onChange={setShowing}
        options={[
          { value: "waiting", label: "Waiting", title: "Changes waiting for approval, oldest first" },
          { value: "decided", label: "Decided", title: "The latest approved or rejected, newest first" },
        ]}
      />

      {changes.isPending ? (
        <LoadingBlock />
      ) : changes.isError ? (
        <LoadError error={changes.error} what="partner changes" onRetry={() => void changes.refetch()} />
      ) : changes.data.length === 0 ? (
        <EmptyState title={waiting ? "Nothing waits for approval" : "Nothing decided yet"}>
          {waiting
            ? "When a partner whose changes need approval saves a change in the portal, it shows here."
            : "Approved and rejected changes show here."}
        </EmptyState>
      ) : (
        <Table
          rows={changes.data}
          rowKey={(c) => `${c.kind}-${c.id}`}
          onRowClick={(c) => setOpened(c)}
          columns={[
            { header: "Partner", wrap: true, cell: (c) => <span className="font-medium text-ink-900">{c.partnerName}</span> },
            { header: "Subscription", wrap: true, cell: (c) => c.subscriptionName ?? "-" },
            {
              header: "Change",
              headerTitle: "Their own version of a mapping, going back to the subscription's own, or their details",
              cell: (c) => what(c),
            },
            {
              header: "Asked by",
              headerTitle: "The partner's person who made the change",
              cell: (c) => c.requestedBy ?? "-",
            },
            {
              header: waiting ? "Asked" : "Decided",
              headerTitle: waiting ? "When they made the change" : "When it was approved or rejected",
              align: "right",
              cell: (c) => {
                const on = waiting ? c.requestedOn : (c.decidedOn ?? c.requestedOn);
                return <span title={formatDateTime(on)}>{timeAgo(on)}</span>;
              },
            },
            ...(waiting
              ? []
              : [
                  {
                    header: "Outcome",
                    headerTitle: "Approved or rejected, and by whom",
                    align: "right" as const,
                    cell: (c: PartnerChange) => (
                      <span className="inline-flex items-center gap-1.5">
                        <PartnerChangeBadge status={c.status} />
                        {c.decidedBy && <span className="text-xs text-ink-500">{c.decidedBy}</span>}
                      </span>
                    ),
                  },
                ]),
          ]}
          card={(c) => ({
            title: c.partnerName,
            status: <PartnerChangeBadge status={c.status} />,
            subtitle: c.subscriptionName ? `${c.subscriptionName} · ${what(c)}` : what(c),
            facts: [{ label: "Asked", value: timeAgo(c.requestedOn) }],
          })}
        />
      )}

      {opened?.kind === "properties" && <PartnerPropertyChangeDialog changeId={opened.id} onClose={() => setOpened(null)} />}
      {opened?.kind === "mapping" && <PartnerChangeDialog changeId={opened.id} onClose={() => setOpened(null)} />}
    </div>
  );
}
