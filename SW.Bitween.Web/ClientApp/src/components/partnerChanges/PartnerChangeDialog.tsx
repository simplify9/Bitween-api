import { lazy, Suspense, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, type PartnerChangeStatus } from "../../api";
import { keys } from "../../api/queryKeys";
import { useSessionCan } from "../../auth/useSessionCan";
import { Badge, Button, FormError, LoadError, LoadingBlock, ProblemText } from "../ui/basics";
import { TextInput } from "../ui/forms";
import { Dialog } from "../ui/overlays";
import { formatDateTime, timeAgo } from "../../lib/dates";

const RulesCompare = lazy(() => import("./RulesCompare"));

const STATUS: Record<PartnerChangeStatus, { label: string; tone: "warn" | "ok" | "danger" | "neutral"; title: string }> = {
  waiting: { label: "Waiting", tone: "warn", title: "Not used until someone approves it." },
  live: { label: "Used at once", tone: "ok", title: "Their changes didn't need approval, or our team made it." },
  approved: { label: "Approved", tone: "ok", title: "Used from their next exchange after it was approved." },
  rejected: { label: "Rejected", tone: "danger", title: "Not used. The partner sees why." },
  withdrawn: { label: "Taken back", tone: "neutral", title: "Taken back before anyone decided: by the partner, or by a newer change." },
};

export function PartnerChangeBadge({ status }: { status: PartnerChangeStatus }) {
  const s = STATUS[status];
  return (
    <Badge tone={s.tone} title={s.title}>
      {s.label}
    </Badge>
  );
}

/**
 * One change a partner made to their own mapping: what they get now beside what it would make it,
 * and, while it waits, Approve and Reject.
 */
export function PartnerChangeDialog({ changeId, onClose }: { changeId: number; onClose: () => void }) {
  const queryClient = useQueryClient();
  const canDecide = useSessionCan("partner-changes.operate");
  const [note, setNote] = useState("");
  const change = useQuery({ queryKey: keys.portal.change(changeId), queryFn: () => api.getPartnerChange(changeId) });

  const decided = async () => {
    // Every list that shows changes or partners' mappings, and the count beside the page's name.
    await queryClient.invalidateQueries({ queryKey: keys.portal.all });
    onClose();
  };
  const approve = useMutation({ mutationFn: () => api.approvePartnerChange(changeId), onSuccess: decided });
  const reject = useMutation({ mutationFn: () => api.rejectPartnerChange(changeId, note.trim()), onSuccess: decided });

  const c = change.data;
  return (
    <Dialog title={c ? `${c.partnerName}: ${c.subscriptionName}` : "Partner change"} onClose={onClose} wide>
      {change.isPending ? (
        <LoadingBlock />
      ) : change.isError ? (
        <LoadError error={change.error} what="this change" onRetry={() => void change.refetch()} />
      ) : (
        <div className="space-y-3">
          <div className="flex flex-wrap items-center gap-2 text-[13px] text-ink-600">
            <PartnerChangeBadge status={c!.status} />
            <span title={formatDateTime(c!.requestedOn)}>
              {c!.requestedBy ?? "Someone"} {c!.status === "waiting" ? "asked" : "made it"} {timeAgo(c!.requestedOn)}
            </span>
            {c!.decidedOn && (
              <span title={formatDateTime(c!.decidedOn)}>
                · {c!.status === "withdrawn" ? "taken back" : c!.status} {timeAgo(c!.decidedOn)}
                {c!.decidedBy ? ` by ${c!.decidedBy}` : ""}
              </span>
            )}
          </div>
          {c!.note && <p className="rounded-lg bg-ink-100 px-3 py-2 text-sm text-ink-700">{c!.note}</p>}

          {c!.backToOurs ? (
            <p className="text-sm text-ink-700">
              {c!.status === "waiting" ? "They ask to drop" : "This dropped"} their own mapping, so they get the
              subscription's own mapping again, the same as everyone else on it.
            </p>
          ) : (
            <Suspense fallback={<LoadingBlock label="Laying out the rules…" />}>
              <RulesCompare
                before={c!.currentRules}
                after={c!.mappingRules}
                beforeLabel={c!.currentIsOurs ? "Used for them now: the subscription's" : "Used for them now: their own"}
                afterLabel={c!.status === "waiting" ? "Their change" : "This change"}
              />
            </Suspense>
          )}

          {c!.problem && <ProblemText>{c!.problem}</ProblemText>}

          {c!.status === "waiting" && canDecide && (
            <div className="space-y-2 border-t border-ink-100 pt-3">
              <div className="flex flex-wrap items-end gap-2">
                <label className="min-w-60 flex-1 space-y-1">
                  <span className="block text-[13px] font-medium text-ink-700">Why, if you reject it</span>
                  <TextInput
                    value={note}
                    maxLength={1000}
                    onChange={(e) => setNote(e.target.value)}
                    placeholder="The partner sees this"
                  />
                </label>
                <Button
                  busy={reject.isPending}
                  disabled={approve.isPending}
                  onClick={() => reject.mutate()}
                  title="They keep what they get now, and see your reason"
                >
                  Reject
                </Button>
                <Button
                  variant="primary"
                  busy={approve.isPending}
                  disabled={!!c!.problem || reject.isPending}
                  onClick={() => approve.mutate()}
                  title={c!.problem ?? "Used for this partner from their next exchange"}
                >
                  Approve
                </Button>
              </div>
              <FormError>{(approve.error ?? reject.error)?.message}</FormError>
            </div>
          )}
        </div>
      )}
    </Dialog>
  );
}
