import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { api, type SubscriptionPartnerMapping } from "../../../api";
import { keys } from "../../../api/queryKeys";
import { Can } from "../../../auth/guards";
import { Badge, Button, LoadError } from "../../../components/ui/basics";
import { HighlightedDocument } from "../../../components/ui/HighlightedDocument";
import { ConfirmDialog, Dialog } from "../../../components/ui/overlays";
import { formatDateTime, timeAgo } from "../../../lib/dates";
import { PartnerChangeDialog } from "../../../components/partnerChanges/PartnerChangeDialog";
import { PartnerMappingHistoryDialog } from "../../../components/partnerChanges/PartnerMappingHistoryDialog";

const pretty = (rules: string) => {
  try {
    return JSON.stringify(JSON.parse(rules), null, 2);
  } catch {
    return rules;
  }
};

/**
 * The partners using their own mapping of this subscription, made in the partner portal. Says why
 * one partner receives something different from the rest, and lets someone who can edit the
 * subscription put a partner back on it. Nothing at all when nobody has their own.
 */
export function PartnerMappings({ subscriptionId }: { subscriptionId: string }) {
  const queryClient = useQueryClient();
  const mappings = useQuery({
    queryKey: keys.portal.ofSubscription(subscriptionId),
    queryFn: () => api.listSubscriptionPartnerMappings(subscriptionId),
  });
  const [seeing, setSeeing] = useState<SubscriptionPartnerMapping | null>(null);
  const [removing, setRemoving] = useState<SubscriptionPartnerMapping | null>(null);
  const [reviewing, setReviewing] = useState<number | null>(null);
  const [history, setHistory] = useState<SubscriptionPartnerMapping | null>(null);

  // A failed read says so, rather than looking like nobody has their own.
  if (mappings.isError)
    return (
      <div className="mt-4 border-t border-ink-100 pt-3">
        <LoadError error={mappings.error} what="partners' own mappings" onRetry={() => void mappings.refetch()} />
      </div>
    );
  if (!mappings.data?.length) return null;

  return (
    <div className="mt-4 border-t border-ink-100 pt-3">
      <h3
        className="text-[13px] font-semibold text-ink-800"
        title="Made by the partner in the partner portal, and used for their exchanges instead of the mapping above."
      >
        Partners with their own mapping
      </h3>
      <p className="mb-2 text-[13px] text-ink-500">
        These get their own version instead of the mapping above. Everyone else gets the one above.
      </p>
      <ul className="divide-y divide-ink-100 rounded-lg border border-ink-200">
        {mappings.data.map((m) => (
          <li key={m.partnerId} className="flex items-center gap-2 px-3 py-2 text-sm">
            <div className="min-w-0 flex-1">
              <div className="flex items-center gap-1.5">
                <span className="truncate font-medium text-ink-900">{m.partnerName}</span>
                {m.waitingChangeId !== null && (
                  <Badge tone="warn" title="A change of theirs waits for approval. Nothing of it is used yet.">
                    Waiting
                  </Badge>
                )}
                {m.setAside && (
                  <Badge
                    tone="warn"
                    title="It reads a global value the mapping above no longer sends, so they get the one above until they change theirs."
                  >
                    Not in use
                  </Badge>
                )}
              </div>
              {m.inUse ? (
                <div className="truncate text-xs text-ink-500" title={formatDateTime(m.changedOn)}>
                  Changed {timeAgo(m.changedOn)}
                  {m.changedBy ? ` by ${m.changedBy}` : ""}
                </div>
              ) : (
                <div className="truncate text-xs text-ink-500">Gets the mapping above until their change is approved</div>
              )}
            </div>
            {m.waitingChangeId !== null && (
              <Can permission="partner-changes.view">
                <Button size="sm" onClick={() => setReviewing(m.waitingChangeId)} title="Their waiting change beside what they get now">
                  Review
                </Button>
              </Can>
            )}
            {m.inUse && (
              <Button size="sm" onClick={() => setSeeing(m)} title="The rules this partner's exchanges are mapped with">
                See rules
              </Button>
            )}
            <Can permission="partner-changes.view">
              <Button size="sm" onClick={() => setHistory(m)} title="Every change to their mapping, and an earlier version to put back">
                History
              </Button>
            </Can>
            {m.inUse && (
              <Can permission="subscriptions.edit">
                <Button
                  size="sm"
                  onClick={() => setRemoving(m)}
                  title="Delete their version, so they get the mapping above from their next exchange"
                >
                  Remove
                </Button>
              </Can>
            )}
          </li>
        ))}
      </ul>

      {seeing && (
        <Dialog title={`${seeing.partnerName}'s mapping`} onClose={() => setSeeing(null)} wide>
          <div className="max-h-[70vh] overflow-auto rounded-lg border border-ink-200 bg-ink-50 p-3 text-[12px]">
            <HighlightedDocument text={pretty(seeing.mappingRules)} format="json" />
          </div>
        </Dialog>
      )}
      {reviewing !== null && <PartnerChangeDialog changeId={reviewing} onClose={() => setReviewing(null)} />}
      {history && (
        <PartnerMappingHistoryDialog
          subscriptionId={subscriptionId}
          partnerId={history.partnerId}
          partnerName={history.partnerName}
          onClose={() => setHistory(null)}
        />
      )}
      {removing && (
        <ConfirmDialog
          title={`Remove ${removing.partnerName}'s own mapping?`}
          body="They get the mapping above from their next exchange. They can make their own version again in the partner portal."
          confirmLabel="Remove their mapping"
          onConfirm={async () => {
            await api.removeSubscriptionPartnerMapping(subscriptionId, removing.partnerId);
            void queryClient.invalidateQueries({ queryKey: keys.portal.all });
          }}
          onClose={() => setRemoving(null)}
        />
      )}
    </div>
  );
}
