import { Plus, X } from "lucide-react";
import type { SubscriptionNotification } from "../../../api";
import { NotificationChannelPicker } from "../../../components/config/pickers";
import { Button } from "../../../components/ui/basics";
import { Panel } from "../../../components/ui/Panel";

const OUTCOMES: { key: "onFailure" | "onBadResult" | "onSuccess"; label: string; title: string }[] = [
  { key: "onFailure", label: "Fails", title: "The exchange errored and didn't complete." },
  { key: "onBadResult", label: "Bad result", title: "The exchange completed, but the response was flagged bad." },
  { key: "onSuccess", label: "Succeeds", title: "The exchange completed with a good response." },
];

/**
 * Who hears about this subscription's finished exchanges. Each row is a channel from Settings and
 * the outcomes it sends on, so failures can go to one team and successes to another. Staged into
 * the page's save bar like the rest of the subscription.
 */
export function NotificationsPanel({
  value,
  onChange,
  canEdit,
}: {
  value: SubscriptionNotification[];
  onChange: (next: SubscriptionNotification[]) => void;
  canEdit: boolean;
}) {
  const update = (index: number, patch: Partial<SubscriptionNotification>) =>
    onChange(value.map((n, i) => (i === index ? { ...n, ...patch } : n)));

  const repeated = new Set(
    value.map((n) => n.channelId).filter((id, i, all) => id > 0 && all.indexOf(id) !== i),
  );

  return (
    <Panel
      title="Notifications"
      description="Tell someone when this subscription's exchanges finish. Channels are set up in Settings."
      action={
        canEdit ? (
          <Button
            size="sm"
            onClick={() => onChange([...value, { channelId: 0, onFailure: true, onBadResult: false, onSuccess: false }])}
          >
            <Plus className="size-3.5" aria-hidden />
            Add
          </Button>
        ) : undefined
      }
    >
      {value.length === 0 ? (
        <p className="text-sm text-ink-500">Nobody is told when its exchanges finish.</p>
      ) : (
        <ul className="divide-y divide-ink-100">
          {value.map((n, i) => {
            const noOutcome = !n.onFailure && !n.onBadResult && !n.onSuccess;
            return (
              <li key={i} className="py-2.5 first:pt-0 last:pb-0">
                <div className="flex flex-wrap items-start gap-x-4 gap-y-2">
                  <div className="w-full max-w-64 min-w-48 flex-1">
                    <NotificationChannelPicker
                      size="sm"
                      value={n.channelId > 0 ? n.channelId : null}
                      disabled={!canEdit}
                      onChange={(channelId) => update(i, { channelId: channelId ?? 0 })}
                    />
                  </div>
                  <div className="flex items-center gap-x-3 pt-1.5">
                    <div className="flex flex-wrap items-center gap-x-3 gap-y-1" role="group" aria-label="Send when it">
                      {OUTCOMES.map((o) => (
                        <label key={o.key} title={o.title} className="flex cursor-pointer items-center gap-1.5 text-[13px] text-ink-700">
                          <input
                            type="checkbox"
                            checked={n[o.key]}
                            disabled={!canEdit}
                            onChange={(e) => update(i, { [o.key]: e.target.checked })}
                            className="size-4 cursor-pointer rounded accent-crimson-600"
                          />
                          {o.label}
                        </label>
                      ))}
                    </div>
                    {canEdit && (
                      <button
                        type="button"
                        onClick={() => onChange(value.filter((_, j) => j !== i))}
                        aria-label="Remove this notification"
                        title="Remove"
                        className="rounded-md p-1 text-ink-400 hover:bg-ink-100 hover:text-ink-700"
                      >
                        <X className="size-3.5" />
                      </button>
                    )}
                  </div>
                </div>
                {n.channelId <= 0 ? (
                  <p className="mt-1 text-xs text-warn-700">Pick a channel, or remove this row.</p>
                ) : noOutcome ? (
                  <p className="mt-1 text-xs text-warn-700">Tick at least one outcome — otherwise it never sends.</p>
                ) : repeated.has(n.channelId) ? (
                  <p className="mt-1 text-xs text-warn-700">This channel is listed twice — tick its outcomes in one row.</p>
                ) : null}
              </li>
            );
          })}
        </ul>
      )}
    </Panel>
  );
}
