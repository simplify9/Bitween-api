import type { ReactNode } from "react";
import { useQuery } from "@tanstack/react-query";
import { api } from "../../api";
import { keys } from "../../api/queryKeys";
import { Badge, LoadError, LoadingBlock } from "../../components/ui/basics";
import { formatDateTime } from "../../lib/dates";

function Group({ title, note, children }: { title: string; note?: string; children: ReactNode }) {
  return (
    <section className="py-4 first:pt-0">
      <h3 className="text-[13px] font-semibold text-ink-900">{title}</h3>
      {note && <p className="mt-0.5 text-[12.5px] text-ink-500">{note}</p>}
      <dl className="mt-2 grid grid-cols-1 gap-x-6 gap-y-2 sm:grid-cols-2">{children}</dl>
    </section>
  );
}

function Item({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="min-w-0">
      <dt className="text-[12px] text-ink-500">{label}</dt>
      <dd className="text-[13px] break-words text-ink-900">{children}</dd>
    </div>
  );
}

const yesNo = (on: boolean, yes = "On", no = "Off") => (
  <Badge tone={on ? "ok" : "neutral"}>{on ? yes : no}</Badge>
);

/**
 * What this Bitween is and how it is set up, read-only. Most of it lives in configuration rather
 * than on the other sections here, so this is the one place to see it without the server's files.
 * Node-level facts are the answering node's: another node may differ.
 */
export function AboutPanel() {
  const about = useQuery({ queryKey: keys.settings.about, queryFn: () => api.getAbout() });
  if (about.isPending) return <LoadingBlock label="Asking this Bitween about itself…" />;
  if (about.isError) return <LoadError error={about.error} what="this instance" onRetry={() => void about.refetch()} />;
  const a = about.data;

  return (
    <div className="divide-y divide-ink-100">
      <Group title="Version">
        <Item label="Bitween">{a.version}</Item>
        {a.build && <Item label="Build">{a.build}</Item>}
      </Group>

      <Group title="This node" note="The node that answered. In a cluster, another node may differ.">
        <Item label="Name">{a.node.name}</Item>
        <Item label="Running since">{formatDateTime(a.node.startedOn)}</Item>
        <Item label="Runs data sources">
          {yesNo(a.node.dataSources, "Yes", "No")}
          {!a.node.dataSources && (
            <span className="ml-1.5 text-[12px] text-ink-500">Bitween:BusProvidersEnabled is off here</span>
          )}
        </Item>
        <Item label="Platform">
          {a.node.runtime} · {a.node.os}
        </Item>
      </Group>

      <Group title="Health" note="The checks /health/ready runs, run now.">
        {a.health.map((h) => (
          <Item key={h.name} label={h.name === "rabbitmq" ? "RabbitMQ" : h.name === "database" ? "Database" : h.name}>
            <Badge tone={h.status === "Healthy" ? "ok" : h.status === "Degraded" ? "warn" : "danger"}>{h.status}</Badge>
            <span className="ml-1.5 text-[12px] text-ink-500">{h.durationMs} ms</span>
            {(h.error || (h.status !== "Healthy" && h.description)) && (
              <p className="mt-0.5 text-[12px] text-danger-700">{h.error ?? h.description}</p>
            )}
          </Item>
        ))}
      </Group>

      <Group title="Where things are">
        <Item label="Database">{a.database}</Item>
        <Item label="Storage">{a.storage.provider}</Item>
        <Item label="Adapter packages">{a.storage.adapterPath}</Item>
        <Item label="Queue prefix">{a.broker.queuePrefix}</Item>
        <Item label="Queue health from RabbitMQ">{yesNo(a.broker.managementConfigured, "Configured", "Not configured")}</Item>
        <Item label="OpenTelemetry export">{yesNo(a.telemetry.openTelemetry)}</Item>
      </Group>

      <Group title="Custom adapters on this node" note="What adapters in each language need to run here.">
        {a.adapters.runtimes.map((r) => (
          <Item key={r.name} label={r.name === "dotnet" ? ".NET" : r.name === "python" ? "Python" : "Node.js"}>
            {r.available ? (
              <>
                <Badge tone="ok">Available</Badge>
                {r.version && <span className="ml-1.5 text-[12px] text-ink-500">{r.version.split("\n")[0]}</span>}
              </>
            ) : (
              <>
                <Badge tone="danger">Missing</Badge>
                {r.reason && <span className="ml-1.5 text-[12px] text-ink-500">{r.reason}</span>}
              </>
            )}
          </Item>
        ))}
        <Item label="Call timeout">{a.adapters.commandTimeoutSeconds} s</Item>
      </Group>

      <Group
        title="Adapter editor"
        note="How the editor builds, checks and tries drafts on the server."
      >
        <Item label="Fetches dependencies">
          {yesNo(a.adapters.editor.dependencies)}
          {a.adapters.editor.dependencies && (!a.adapters.pip.available || !a.adapters.npm.available) && (
            <p className="mt-0.5 text-[12px] text-warn-800">
              {!a.adapters.pip.available ? "pip" : "npm"} isn't on this node, so drafts that need packages in{" "}
              {!a.adapters.pip.available ? "Python" : "JavaScript"} can't be built here.
            </p>
          )}
        </Item>
        <Item label="Memory per run">{a.adapters.editor.memoryMb > 0 ? `${a.adapters.editor.memoryMb} MB` : "No limit"}</Item>
        <Item label="CPU per run">
          {a.adapters.editor.cpuCores > 0
            ? `${a.adapters.editor.cpuCores} core${a.adapters.editor.cpuCores === 1 ? "" : "s"}`
            : "No limit"}
        </Item>
        <Item label="pip / npm">
          {a.adapters.pip.available ? "pip" : "no pip"} · {a.adapters.npm.available ? "npm" : "no npm"}
        </Item>
      </Group>

      <Group title="Limits">
        <Item label="Sign-in attempts">{a.limits.signInPerMinute} a minute per address</Item>
        <Item label="API requests">{a.limits.requestsPerMinute} a minute per address</Item>
        <Item label="File link downloads">{a.limits.fileLinksPerMinute} a minute</Item>
        <Item label="Longest wait for a result">{a.limits.maxResponseWaitSeconds} s</Item>
        <Item label="Longest retry chain">{a.limits.maxRetryChainDepth} attempts</Item>
        <Item label="A run counts as stuck after">{a.limits.staleRunAfterMinutes} min</Item>
      </Group>

      <Group title="Network">
        <Item label="Public address">{a.network.publicUrl || <span className="text-ink-500">Not set</span>}</Item>
        <Item label="Blocks private addresses for outbound calls">{yesNo(a.network.blockPrivateNetworkAddresses)}</Item>
        <Item label="Trusted proxies">{a.network.trustedProxies || "None"}</Item>
        <Item label="API docs (Swagger)">{yesNo(a.network.exposeApiDocs, "Exposed", "Hidden")}</Item>
      </Group>

      <Group title="Housekeeping">
        <Item label="Receive attempts kept">{a.retention.receiveAttemptRetentionDays} days</Item>
        <Item label="Receive attempt clean-up">
          <code className="font-mono text-[12px]">{a.retention.receiveAttemptCleanupCron}</code>
        </Item>
        <Item label="Broker deduplication clean-up">
          <code className="font-mono text-[12px]">{a.retention.inboundMessagePruneCron}</code>
        </Item>
      </Group>
    </div>
  );
}
