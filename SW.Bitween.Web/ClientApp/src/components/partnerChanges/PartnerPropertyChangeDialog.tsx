import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "../../api";
import { keys } from "../../api/queryKeys";
import { useSessionCan } from "../../auth/useSessionCan";
import { Badge, Button, FormError, LoadError, LoadingBlock, ProblemText } from "../ui/basics";
import { TextInput } from "../ui/forms";
import { Dialog } from "../ui/overlays";
import { timeAgo, formatDateTime } from "../../lib/dates";
import { PartnerChangeBadge } from "./PartnerChangeDialog";

/**
 * One change a partner made to their own properties: each value now beside what it would become, a
 * secret one never shown, and, while it waits, Approve and Reject.
 */
export function PartnerPropertyChangeDialog({ changeId, onClose }: { changeId: number; onClose: () => void }) {
  const queryClient = useQueryClient();
  const canDecide = useSessionCan("partner-changes.operate");
  const [note, setNote] = useState("");
  const change = useQuery({
    queryKey: keys.portal.propertyChange(changeId),
    queryFn: () => api.getPartnerPropertyChange(changeId),
  });
  const decided = async () => {
    await queryClient.invalidateQueries({ queryKey: keys.portal.all });
    await queryClient.invalidateQueries({ queryKey: keys.partners.all });
    onClose();
  };
  const approve = useMutation({ mutationFn: () => api.approvePartnerPropertyChange(changeId), onSuccess: decided });
  const reject = useMutation({ mutationFn: () => api.rejectPartnerPropertyChange(changeId, note.trim()), onSuccess: decided });

  const c = change.data;
  return (
    <Dialog title={c ? `${c.partnerName}: their details` : "Partner change"} onClose={onClose} wide>
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

          <table className="w-full text-sm">
            <thead>
              <tr className="text-left text-[11.5px] font-medium tracking-wide text-ink-500 uppercase">
                <th className="py-1 pr-3" title="The partner property">Property</th>
                <th className="py-1 pr-3" title="Its value now">Now</th>
                <th className="py-1" title="What the change makes it">{c!.status === "waiting" ? "Their change" : "This change"}</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-ink-100">
              {c!.items.map((i) => (
                <tr key={i.key}>
                  <td className="py-1.5 pr-3 font-mono text-xs text-ink-700">{i.key}</td>
                  {i.secret ? (
                    <td colSpan={2} className="py-1.5">
                      <Badge title="A secret: neither value is shown, here or anywhere.">Secret, replaced</Badge>
                    </td>
                  ) : (
                    <>
                      <td className="py-1.5 pr-3 break-all text-ink-600">{i.current || "-"}</td>
                      <td className="py-1.5 break-all font-medium text-ink-900">{i.proposed || "-"}</td>
                    </>
                  )}
                </tr>
              ))}
            </tbody>
          </table>

          {c!.problem && <ProblemText>{c!.problem}</ProblemText>}

          {c!.status === "waiting" && canDecide && (
            <div className="space-y-2 border-t border-ink-100 pt-3">
              <div className="flex flex-wrap items-end gap-2">
                <label className="min-w-60 flex-1 space-y-1">
                  <span className="block text-[13px] font-medium text-ink-700">Why, if you reject it</span>
                  <TextInput value={note} maxLength={1000} onChange={(e) => setNote(e.target.value)} placeholder="The partner sees this" />
                </label>
                <Button busy={reject.isPending} disabled={approve.isPending} onClick={() => reject.mutate()} title="They keep what they have now, and see your reason">
                  Reject
                </Button>
                <Button
                  variant="primary"
                  busy={approve.isPending}
                  disabled={!!c!.problem || reject.isPending}
                  onClick={() => approve.mutate()}
                  title={c!.problem ?? "Their properties change now"}
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
