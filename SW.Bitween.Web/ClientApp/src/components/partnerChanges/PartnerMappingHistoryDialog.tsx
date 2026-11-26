import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { api, type PartnerChangeVersion } from "../../api";
import { keys } from "../../api/queryKeys";
import { useSessionCan } from "../../auth/useSessionCan";
import { Button, LoadError, LoadingBlock } from "../ui/basics";
import { ConfirmDialog, Dialog } from "../ui/overlays";
import { formatDateTime, timeAgo } from "../../lib/dates";
import { PartnerChangeBadge, PartnerChangeDialog } from "./PartnerChangeDialog";

/**
 * A partner's mapping of one subscription over time, newest first: each change, who made it and what
 * became of it. A version that was used for them can be put back, for when a later one broke things.
 */
export function PartnerMappingHistoryDialog({
  subscriptionId,
  partnerId,
  partnerName,
  onClose,
}: {
  subscriptionId: string;
  partnerId: number;
  partnerName: string;
  onClose: () => void;
}) {
  const queryClient = useQueryClient();
  const canPutBack = useSessionCan("partner-changes.operate");
  const history = useQuery({
    queryKey: keys.portal.history(subscriptionId, partnerId),
    queryFn: () => api.listPartnerMappingHistory(subscriptionId, partnerId),
  });
  const [opened, setOpened] = useState<number | null>(null);
  const [puttingBack, setPuttingBack] = useState<PartnerChangeVersion | null>(null);
  // The newest change that was used is what they get now; putting that back would change nothing.
  const currentId = history.data?.find((v) => v.status === "live" || v.status === "approved")?.id;

  return (
    <Dialog title={`${partnerName}'s mapping history`} onClose={onClose} wide>
      {history.isPending ? (
        <LoadingBlock />
      ) : history.isError ? (
        <LoadError error={history.error} what="the history" onRetry={() => void history.refetch()} />
      ) : history.data.length === 0 ? (
        <p className="text-sm text-ink-500">No changes recorded yet.</p>
      ) : (
        <ul className="divide-y divide-ink-100 rounded-lg border border-ink-200">
          {history.data.map((v) => (
            <li key={v.id} className="flex items-center gap-2 px-3 py-2 text-sm">
              <div className="min-w-0 flex-1">
                <div className="flex flex-wrap items-center gap-1.5">
                  <span className="font-medium text-ink-900">
                    {v.backToOurs ? "Back to the subscription's mapping" : "Their own mapping"}
                  </span>
                  <PartnerChangeBadge status={v.status} />
                </div>
                <div className="truncate text-xs text-ink-500" title={formatDateTime(v.requestedOn)}>
                  {timeAgo(v.requestedOn)}
                  {v.requestedBy ? ` by ${v.requestedBy}` : ""}
                  {v.decidedBy && v.status !== "withdrawn" ? `, ${v.status} by ${v.decidedBy}` : ""}
                  {v.note ? ` · ${v.note}` : ""}
                </div>
              </div>
              <Button size="sm" onClick={() => setOpened(v.id)} title="What this change asked for">
                See
              </Button>
              {canPutBack && v.canPutBack && v.id !== currentId && (
                <Button
                  size="sm"
                  onClick={() => setPuttingBack(v)}
                  title="Use this version for them again, from their next exchange"
                >
                  Use this version again
                </Button>
              )}
            </li>
          ))}
        </ul>
      )}

      {opened !== null && <PartnerChangeDialog changeId={opened} onClose={() => setOpened(null)} />}
      {puttingBack && (
        <ConfirmDialog
          title="Use this version again?"
          body={`${partnerName} gets the version from ${formatDateTime(puttingBack.requestedOn)} from their next exchange. It's used at once, without waiting for approval.`}
          confirmLabel="Use this version"
          confirmVariant="primary"
          onConfirm={async () => {
            await api.putBackPartnerMapping(puttingBack.id);
            await queryClient.invalidateQueries({ queryKey: keys.portal.all });
          }}
          onClose={() => setPuttingBack(null)}
        />
      )}
    </Dialog>
  );
}
