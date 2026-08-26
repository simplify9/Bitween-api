import { useState } from "react";
import { Link } from "react-router";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { RotateCcw } from "lucide-react";
import { api } from "../../../api";
import { Button } from "../../../components/ui/basics";
import { ConfirmDialog } from "../../../components/ui/overlays";
import { timeAgo } from "../../../lib/dates";

/**
 * This subscription's own retry budgets, asked for from its side rather than its policy's.
 *
 * Two reasons it is not enough to read this on the retry policy page. A subscription can carry
 * rules inline instead of a shared policy — those have no policy id, so no policy page reaches
 * their counters, and one could sit stopped for good with nothing on screen able to say why or
 * hand it back. And even with a shared policy, "why has this stopped retrying?" gets asked
 * here, where the subscription is, not on a page listing every subscription that shares its rules.
 *
 * Silent until it has something to report: a subscription that has never failed does not need
 * to be told it has spent none of its budget.
 */
export function RetryBudget({ subscriptionId, canEdit }: { subscriptionId: number; canEdit: boolean }) {
  const queryClient = useQueryClient();
  const [resetting, setResetting] = useState(false);

  const usage = useQuery({
    queryKey: ["retry-usage", "subscription", subscriptionId],
    queryFn: () => api.getSubscriptionRetryUsage(subscriptionId),
  });

  const rows = (usage.data ?? []).filter((r) => r.used > 0);
  if (rows.length === 0) return null;

  const stopped = rows.filter((r) => r.exhausted);
  const lastFailure = rows
    .map((r) => r.lastAttemptOn)
    .filter((d): d is string => d !== null)
    .sort()
    .at(-1);

  return (
    <div
      className={`rounded-xl px-4 py-2.5 text-[13px] ${
        stopped.length > 0 ? "bg-danger-50 text-danger-800" : "bg-ink-50 text-ink-600"
      }`}
    >
      <div className="flex flex-wrap items-center justify-between gap-x-4 gap-y-2">
        <p>
          {stopped.length > 0 ? (
            <>
              <strong className="font-medium">Retries have stopped.</strong>{" "}
              {stopped.map((r) => r.groupName).join(", ")} used up{" "}
              {stopped.length === 1 ? "its budget" : "their budgets"} — failures are no longer
              retried automatically until the budget is reset, or this subscription succeeds again.
            </>
          ) : (
            <>
              Retry budget:{" "}
              {rows.map((r) => `${r.groupName} ${r.used}/${r.total}`).join(", ")}
              {lastFailure && ` · last failure ${timeAgo(lastFailure)}`}
            </>
          )}
        </p>
        <span className="flex shrink-0 items-center gap-3">
          {stopped.some((r) => r.resolvedHandlerId === null) && (
            <span className="text-[13px]">Nobody was alerted.</span>
          )}
          <Link
            to={`/exchanges?subscriptionId=${subscriptionId}&status=failed`}
            className="font-medium text-crimson-700 hover:underline"
          >
            See failures
          </Link>
          {canEdit && stopped.length > 0 && (
            <Button size="sm" onClick={() => setResetting(true)}>
              <RotateCcw className="size-3.5" /> Reset
            </Button>
          )}
        </span>
      </div>

      {resetting && (
        <ConfirmDialog
          title="Reset this subscription's retry budgets?"
          body={
            <>
              Retries resume immediately for {stopped.map((r) => r.groupName).join(", ")}. If
              whatever they were failing against is still down, the budget will be spent again.
            </>
          }
          confirmLabel="Reset budgets"
          onConfirm={async () => {
            // No group id: every group of this subscription, which is what the banner reports on.
            await api.resetSubscriptionRetryUsage(subscriptionId);
            await queryClient.invalidateQueries({ queryKey: ["retry-usage"] });
          }}
          onClose={() => setResetting(false)}
        />
      )}
    </div>
  );
}
