import type { KvRow } from "./KeyValueEditor";

/** Record<string,string> ⇄ ordered rows helpers. */
export const toRows = (record: Record<string, string>): KvRow[] =>
  Object.entries(record).map(([key, value]) => ({ key, value }));

export const toRecord = (rows: KvRow[]): Record<string, string> =>
  Object.fromEntries(rows.filter((r) => r.key.trim()).map((r) => [r.key.trim(), r.value]));
