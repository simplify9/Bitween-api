import type { ReactNode } from "react";
import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { api } from "../../api";
import { Badge } from "../../components/ui/basics";
import { queueHealthTitle } from "../../components/config/shared";
import { useRabbitMqManagementConfigured } from "../../lib/appConfig";

function LiveStat({ label, value, tone }: { label: string; value: ReactNode; tone?: "warn" | "danger" }) {
  return (
    <div className="min-w-0">
      <dt className="text-[11px] leading-tight font-medium tracking-wide text-ink-400 uppercase">{label}</dt>
      <dd
        className={`mt-0.5 text-sm font-semibold tabular-nums ${
          tone === "danger" ? "text-danger-700" : tone === "warn" ? "text-warn-700" : "text-ink-800"
        }`}
      >
        {value}
      </dd>
    </div>
  );
}

/**
 * One work group's slice of the live RabbitMQ picture — the same numbers
 * the Queue health page shows, on the group's own page. The list page renders
 * these as columns instead, off the same shared query.
 */
export function LiveQueueStats({ groupId }: { groupId: number }) {
  const rabbitMqConfigured = useRabbitMqManagementConfigured();
  const { data } = useQuery({
    queryKey: ["queue-health"],
    queryFn: () => api.getQueueHealth(),
    refetchInterval: 5_000,
    placeholderData: keepPreviousData,
    enabled: rabbitMqConfigured,
  });

  if (!rabbitMqConfigured)
    return (
      <p className="text-sm text-ink-500">
        Live queue stats need RabbitMQ management configured on the backend.
      </p>
    );

  const consumer = data?.consumers.find((c) => c.workGroupId === groupId);

  if (!consumer)
    return <p className="text-sm text-ink-500">No live consumer data for this group right now.</p>;

  return (
    <div className="@container space-y-3">
      <div className="flex flex-wrap items-center gap-1.5">
        {consumer.health === "critical" ? (
          <Badge tone="danger" title={queueHealthTitle("critical")}>Critical</Badge>
        ) : consumer.health === "warning" ? (
          <Badge tone="warn" title={queueHealthTitle("warning")}>Warning</Badge>
        ) : (
          <Badge tone="ok" title={queueHealthTitle("healthy")}>Healthy</Badge>
        )}
        {consumer.isBackpressured && (
          <Badge
            tone="warn"
            title="Queue depth has passed its configured threshold — messages are arriving faster than they're being consumed."
          >
            Backpressure
          </Badge>
        )}
        <code className="font-mono text-[11px] text-ink-400">{consumer.queueName}</code>
      </div>
      <dl className="grid grid-cols-3 gap-x-4 gap-y-3 @sm:grid-cols-5 @2xl:grid-cols-9">
        <LiveStat label="Nodes" value={consumer.totalNodes} />
        <LiveStat label="In flight" value={consumer.processingCount} />
        <LiveStat label="Queued" value={consumer.queueCount} />
        <LiveStat label="Retrying" value={consumer.retryCount} tone={consumer.retryCount > 0 ? "warn" : undefined} />
        <LiveStat
          label="Dead letters"
          value={consumer.failedCount}
          tone={consumer.failedCount > 0 ? "danger" : undefined}
        />
        <LiveStat label="Prefetch" value={consumer.prefetch} />
        <LiveStat label="Priority" value={consumer.priority} />
        <LiveStat label="Incoming" value={`${consumer.incomingRate}/s`} />
        <LiveStat label="Acked" value={`${consumer.ackRate}/s`} />
      </dl>
    </div>
  );
}
