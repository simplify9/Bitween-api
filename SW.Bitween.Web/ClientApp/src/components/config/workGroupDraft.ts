import { useQuery } from "@tanstack/react-query";
import { api, type WorkGroup } from "../../api";
import { useSessionCan } from "../../auth/useSessionCan";
import { keys } from "../../api/queryKeys";
import { useRabbitMqManagementConfigured } from "../../lib/appConfig";
import type { WorkGroupDraft } from "./WorkGroupDialog";

export const workGroupDraftOf = (g: WorkGroup): WorkGroupDraft => ({
  name: g.name,
  busMessageName: g.busMessageName,
  prefetch: g.options.rabbitMqOptions.consumerSettings.prefetch,
  priority: g.options.rabbitMqOptions.consumerSettings.priority,
});

/**
 * Messages still in a group's queues — both of its lanes, retries and dead letters included —
 * from the same live snapshot Queue health polls. `null` when it can't be known: no right to see
 * queue health, RabbitMQ management not configured, or not loaded yet.
 */
export function useQueuedMessages(groupId: number | null): number | null {
  const canMonitor = useSessionCan("monitoring.view");
  const rabbitMqConfigured = useRabbitMqManagementConfigured();
  const { data } = useQuery({
    queryKey: keys.queueHealth,
    queryFn: () => api.getQueueHealth(),
    enabled: groupId !== null && canMonitor && rabbitMqConfigured,
  });
  if (groupId === null || !data) return null;
  return data.consumers
    .filter((c) => c.workGroupId === groupId)
    .reduce((n, c) => n + c.queueCount + c.retryCount + c.failedCount, 0);
}

/** Whether a new bus message name means new queues. Case alone doesn't: queue names are lowercase. */
export const renamesQueues = (from: string, to: string): boolean => from.toLowerCase() !== to.toLowerCase();
