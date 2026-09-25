import { useQuery } from "@tanstack/react-query";
import { api } from "../../api";
import { Badge, LoadingBlock } from "../../components/ui/basics";
import { keys } from "../../api/queryKeys";

/**
 * What the connection is doing, as opposed to how it is configured.
 *
 * Two independent sources, kept visually apart because they fail differently.
 * **Adapter-reported** — queue depths, messages handled — arrives on the heartbeat and stops
 * arriving the moment the adapter wedges. **Host-observed** — memory, CPU, restarts — needs no
 * cooperation from the adapter at all, so it still answers when the other half has gone quiet.
 * That distinction is the whole reason an operator can tell "the broker is idle" from "the adapter
 * is stuck", which no single health badge can say.
 *
 * Rendered inside the page's Connection card, which already shows the state badge and the last
 * error — so neither is repeated here unless the live figure says something different.
 */
export function LiveConnection({
  dataSourceId,
  knownError,
}: {
  dataSourceId: number;
  knownError?: string | null;
}) {
  const telemetry = useQuery({
    queryKey: keys.dataSources.telemetry(dataSourceId),
    queryFn: () => api.getDataSourceTelemetry(dataSourceId),
    // Fast enough to watch a queue drain, slow enough not to be a load test of the heartbeat.
    refetchInterval: 3_000,
    retry: false,
  });

  if (telemetry.isPending) return <LoadingBlock label="Reading the heartbeat…" />;
  if (telemetry.isError || !telemetry.data) return null;

  const t = telemetry.data;

  // Queue depths come through as depth:<queue>, which is the one part of the adapter's untyped
  // detail bag worth promoting: backlog per queue is the number an operator actually watches.
  const depths = Object.entries(t.details)
    .filter(([k]) => k.startsWith("depth:"))
    .map(([k, v]) => [k.slice("depth:".length), v] as const);

  const counters = ["received", "acked", "nacked", "failed", "deleted", "returned", "sent"]
    .filter((k) => k in t.details)
    .map((k) => [k, t.details[k]] as const);

  const rest = Object.entries(t.details).filter(
    ([k]) => !k.startsWith("depth:") && !counters.some(([c]) => c === k),
  );

  return (
    <div className="mt-4 border-t border-ink-100 pt-4">
      <div className="mb-3 flex flex-wrap items-center gap-3">
        <h3 className="text-[12px] font-medium tracking-wide text-ink-500 uppercase">Live</h3>
        {t.quarantined && (
          <Badge tone="danger" title="Restarted too many times too quickly; the supervisor stopped trying.">
            Quarantined
          </Badge>
        )}
        {t.missedHeartbeats > 0 && (
          <Badge tone="warn" title="The adapter has stopped answering on time.">
            {t.missedHeartbeats} missed heartbeat{t.missedHeartbeats === 1 ? "" : "s"}
          </Badge>
        )}
      </div>

      {!t.runningHere ? (
        <p className="text-sm text-ink-600">
          {t.ownedByNode
            ? `Held by ${t.ownedByNode}, which is not this node — a broker connection is exclusive, so only that node can see its live figures.`
            : "Not running on any node. External bus providers are opt-in per node, so this is a setting rather than a fault."}
        </p>
      ) : (
        <div className="flex flex-col gap-4">
          {depths.length > 0 && (
            <div>
              <h3 className="mb-1.5 text-[12px] font-medium tracking-wide text-ink-500 uppercase">
                Backlog per queue
              </h3>
              <div className="flex flex-wrap gap-2">
                {depths.map(([queue, depth]) => (
                  <span
                    key={queue}
                    className="inline-flex items-baseline gap-2 rounded-lg border border-ink-200 px-2.5 py-1"
                  >
                    <code className="font-mono text-[11px] text-ink-600">{queue}</code>
                    <span className="text-sm font-semibold tabular-nums text-ink-900">{depth}</span>
                  </span>
                ))}
              </div>
            </div>
          )}

          {counters.length > 0 && (
            <div>
              <h3 className="mb-1.5 text-[12px] font-medium tracking-wide text-ink-500 uppercase">
                Adapter-reported
              </h3>
              <div className="flex flex-wrap gap-x-6 gap-y-1">
                {counters.map(([name, value]) => (
                  <Stat key={name} label={name} value={value} />
                ))}
                <Stat label="in flight" value={String(t.inFlight)} />
                {t.lastMessageOn && (
                  <Stat label="last message" value={new Date(t.lastMessageOn).toLocaleTimeString()} />
                )}
              </div>
            </div>
          )}

          <div>
            <h3 className="mb-1.5 text-[12px] font-medium tracking-wide text-ink-500 uppercase">
              Host-observed
            </h3>
            <p className="mb-1.5 text-[12px] text-ink-500">
              Measured from outside the adapter, so these still answer when it has stopped
              reporting.
            </p>
            <div className="flex flex-wrap gap-x-6 gap-y-1">
              <Stat label="pid" value={t.processId ? String(t.processId) : "—"} />
              <Stat label="memory" value={mb(t.workingSetBytes)} />
              <Stat label="cpu" value={`${t.cpuPercent.toFixed(1)}%`} />
              <Stat label="threads" value={String(t.threadCount)} />
              <Stat label="uptime" value={duration(t.uptime)} />
              <Stat label="restarts" value={String(t.restartCount)} />
            </div>
          </div>

          {rest.length > 0 && (
            <details className="text-sm">
              <summary className="cursor-pointer text-[12px] text-ink-500 hover:text-ink-800">
                Everything else the adapter reports
              </summary>
              <dl className="mt-2 grid grid-cols-1 gap-x-8 gap-y-1 sm:grid-cols-2">
                {rest.map(([k, v]) => (
                  <div key={k} className="flex gap-2">
                    <dt className="shrink-0 text-ink-500">{k}</dt>
                    <dd className="truncate text-ink-800" title={v}>
                      {v}
                    </dd>
                  </div>
                ))}
              </dl>
            </details>
          )}

          {t.lastError && t.lastError !== knownError && (
            <p className="text-sm text-danger-800">{t.lastError}</p>
          )}
        </div>
      )}
    </div>
  );
}

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <span className="inline-flex items-baseline gap-1.5">
      <span className="text-[12px] text-ink-500">{label}</span>
      <span className="text-sm font-medium tabular-nums text-ink-900">{value}</span>
    </span>
  );
}

const mb = (bytes: number) => (bytes > 0 ? `${(bytes / 1024 / 1024).toFixed(0)} MB` : "—");

/** The backend serializes a TimeSpan as "d.hh:mm:ss.fffffff" or "hh:mm:ss.fffffff". */
const duration = (value: string): string => {
  const m = /^(?:(\d+)\.)?(\d{2}):(\d{2}):(\d{2})/.exec(value ?? "");
  if (!m) return "—";
  const [, d, hh, mm, ss] = m;
  if (d && Number(d) > 0) return `${d}d ${Number(hh)}h`;
  if (Number(hh) > 0) return `${Number(hh)}h ${Number(mm)}m`;
  if (Number(mm) > 0) return `${Number(mm)}m ${Number(ss)}s`;
  return `${Number(ss)}s`;
};
