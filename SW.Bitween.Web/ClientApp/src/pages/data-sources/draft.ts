import type { DataSourceDetail } from "../../api/types";

/** Everything on the data source form an operator can change. */
export interface Draft {
  name: string;
  inactive: boolean;
  /** Auto, Exclusive or PerNode. Sent with every save: a save without it used to reset it to Auto. */
  placement: string;
  deduplicationWindowDays: number;
  softMemoryLimitMb: number;
  hardMemoryLimitMb: number;
  cpuPercentLimit: number;
  cpuLimitSamples: number;
  properties: Record<string, string>;
}

export const draftOf = (d: DataSourceDetail): Draft => ({
  name: d.name,
  inactive: d.inactive,
  placement: d.placement,
  deduplicationWindowDays: d.deduplicationWindowDays,
  softMemoryLimitMb: d.softMemoryLimitMb,
  hardMemoryLimitMb: d.hardMemoryLimitMb,
  cpuPercentLimit: d.cpuPercentLimit,
  cpuLimitSamples: d.cpuLimitSamples,
  properties: { ...d.properties },
});

/**
 * Identifies the server's copy of the EDITABLE surface, and nothing else.
 *
 * The detail endpoint is polled so the connection panel stays live, and every poll brings a fresh
 * heartbeat time, state and failure count. Re-seeding the form from each of those responses threw
 * away whatever was being typed — a setting added and not yet saved vanished within ten seconds,
 * with no error and nothing to suggest the form had done it.
 *
 * Comparing this instead means a poll that changed only the live figures leaves the form alone,
 * while a genuine change to the settings — a save, which re-masks the secrets, or another node's
 * edit — still re-seeds it.
 */
export const editableFingerprint = (d: DataSourceDetail): string => JSON.stringify(draftOf(d));
