import { useQuery } from "@tanstack/react-query";
import { api } from "../../api";
import { keys } from "../../api/queryKeys";
import { Badge } from "../../components/ui/basics";
import { Panel } from "../../components/ui/Panel";
import { formatDateTime, timeAgo } from "../../lib/dates";

/** What each refusal means, for whoever is on the phone with the partner. */
const REASON: Record<string, { label: string; meaning: string }> = {
  "not-authenticated": {
    label: "Key not recognised",
    meaning: "No key, or one that isn't any partner's: a typo, a revoked key, or the wrong header.",
  },
  "not-attached": {
    label: "Partner not attached",
    meaning: "A valid key, but its partner isn't attached to this gateway.",
  },
  "gateway-off": { label: "Gateway switched off", meaning: "The gateway is inactive; callers got 503." },
  "no-subscription": {
    label: "Nothing behind it",
    meaning: "The partner's subscription for this gateway is gone.",
  },
  "rate-limited": {
    label: "Too many calls",
    meaning: "More calls a minute from one address than Bitween allows; they got 429.",
  },
  "unknown-gateway": { label: "Unknown address", meaning: "Called at an address no gateway has." },
};

/**
 * Calls to this gateway turned away, and why — what "the partner says they get 401" needed and had
 * nowhere to look. Counted in memory on the node that answers, since it started.
 */
export function GatewayRejections({ gatewayId }: { gatewayId: number }) {
  const rejections = useQuery({
    queryKey: keys.apiGateways.rejections(gatewayId),
    queryFn: () => api.getGatewayRejections(gatewayId),
    refetchInterval: 30_000,
  });
  if (!rejections.data) return null;
  const r = rejections.data;
  const reasons = Object.entries(r.counts).sort((a, b) => b[1] - a[1]);

  return (
    <Panel
      title="Calls turned away"
      description={`Counted by the node that answered (${r.node}) since it started ${timeAgo(r.since)}. Each node counts its own.`}
    >
      {reasons.length === 0 ? (
        <p className="text-[13px] text-ink-500">None. Every call that reached this node got through.</p>
      ) : (
        <div className="space-y-3">
          <ul className="space-y-1.5">
            {reasons.map(([reason, count]) => (
              <li key={reason} className="flex items-start gap-2 text-[13px]">
                <Badge tone="danger">{count}</Badge>
                <span>
                  <span className="font-medium text-ink-900">{REASON[reason]?.label ?? reason}</span>
                  <span className="block text-[12px] text-ink-500">{REASON[reason]?.meaning}</span>
                </span>
              </li>
            ))}
          </ul>
          <div>
            <h4 className="mb-1 text-[12px] font-semibold tracking-wide text-ink-500 uppercase">Most recent</h4>
            <ul className="divide-y divide-ink-100 text-[12.5px]">
              {r.recent.map((x, i) => (
                <li key={i} className="flex flex-wrap justify-between gap-2 py-1">
                  <span className="text-ink-800">
                    {REASON[x.reason]?.label ?? x.reason} <span className="text-ink-500">· {x.status}</span>
                  </span>
                  <span className="text-ink-500" title={formatDateTime(x.on)}>
                    {x.address ?? "unknown address"} · {timeAgo(x.on)}
                  </span>
                </li>
              ))}
            </ul>
          </div>
        </div>
      )}
    </Panel>
  );
}
