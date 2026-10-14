import type { AuditChange, TrailEntry } from "../api";
import { isBookkeeping, isMachineKept, propertyLabel } from "./auditLabels";

/**
 * How a history row reads to a person rather than to the change tracker. The rows themselves are
 * unchanged; this decides which of their fields are worth naming, what the change is called, and
 * which run of identical machine writes is really one line.
 */

type Action = TrailEntry["action"];

/** "Added" and "Modified" were the change tracker's words; these are the ones the screens use. */
export const ACTION_LABEL: Record<Action, string> = {
  Added: "Created",
  Modified: "Changed",
  Deleted: "Deleted",
};

/** The `Badge` tone for each: only a delete is worth red. */
export const ACTION_TONE: Record<Action, "ok" | "warn" | "danger"> = {
  Added: "ok",
  Modified: "warn",
  Deleted: "danger",
};

/**
 * What a person chose when making one of these, in the order worth reading. A created row is a
 * snapshot of every column, so without an order its line named whatever the class declared first:
 * "Rolls up on, Rolls up, Aggregation target +35 more".
 */
const LEADING = [
  "Name",
  "DisplayName",
  "Email",
  "Code",
  "Type",
  "DocumentId",
  "PartnerId",
  "HandlerId",
  "ReceiverId",
  "Schedules",
  "UrlName",
  "Value",
];

/** Nothing worth naming in a new row: the column's default, not a choice. */
const isBlank = (v: unknown): boolean =>
  v === null ||
  v === undefined ||
  v === "" ||
  v === false ||
  v === 0 ||
  (Array.isArray(v) && v.length === 0) ||
  (typeof v === "object" && !Array.isArray(v) && Object.keys(v).length === 0);

const rank = (property: string) => {
  const i = LEADING.indexOf(property);
  return i === -1 ? LEADING.length : i;
};

export interface ChangeSummary {
  /** Everything behind the popover: the change minus the database's own bookkeeping. */
  changes: AuditChange[];
  /** At most three field names for the line itself. */
  shown: string[];
  /** How many more fields a person might care about. */
  more: number;
}

/**
 * The fields one change names. A creation names what was filled in, most telling first, and leaves
 * out what the system keeps for itself; an edit names what changed, with machine-kept fields named
 * only when nothing else changed (the scheduler writing its next run is still worth a line).
 */
export function summarizeChange(entity: string | undefined, action: Action, all: AuditChange[]): ChangeSummary {
  let changes = all.filter((c) => !isBookkeeping(c.property));
  if (action === "Added") changes = changes.filter((c) => !isBlank(c.new));

  const byPerson = changes.filter((c) => !isMachineKept(entity, c.property));
  const named = byPerson.length > 0 ? byPerson : changes;
  const ordered = action === "Added" ? [...named].sort((a, b) => rank(a.property) - rank(b.property)) : named;

  const shown = ordered.slice(0, 3).map((c) => propertyLabel(entity, c.property));
  return { changes, shown, more: ordered.length - shown.length };
}

/** The writers with no person behind them, as the trail spells them ("#scheduled-job"). */
const MACHINE_ACTOR: Record<string, string> = {
  "#scheduled-job": "Scheduler",
};

/**
 * A system writer, as opposed to a member. "#42" is a member whose account has since gone, so it
 * stays a person; "#scheduled-job" and "System" are not.
 */
export const isMachineWrite = (e: Pick<TrailEntry, "by" | "byUserId">): boolean =>
  !e.byUserId && (e.by === "System" || (e.by.startsWith("#") && !/^#\d+$/.test(e.by)));

/** "Scheduler" for "#scheduled-job"; any other system writer loses its "#" and its dashes. */
export function actorName(e: Pick<TrailEntry, "by" | "byUserId">): string {
  if (!isMachineWrite(e)) return e.by;
  const known = MACHINE_ACTOR[e.by];
  if (known) return known;
  const words = e.by.replace(/^#/, "").replace(/[-_]+/g, " ");
  return words.charAt(0).toUpperCase() + words.slice(1);
}

/** Back-to-back writes by the same machine to the same fields of the same row. */
export interface MachineRun {
  kind: "run";
  /** The newest entry's id, so the run keeps its key as older entries page in behind it. */
  id: string;
  entries: TrailEntry[];
}

const fieldsOf = (e: TrailEntry) =>
  e.changes
    .map((c) => c.property)
    .filter((p) => !isBookkeeping(p))
    .sort()
    .join(",");

const sameRun = (a: TrailEntry, b: TrailEntry) =>
  a.action === "Modified" &&
  b.action === "Modified" &&
  isMachineWrite(a) &&
  a.by === b.by &&
  b.byUserId === a.byUserId &&
  a.entityName === b.entityName &&
  a.entityKey === b.entityKey &&
  fieldsOf(a) === fieldsOf(b);

/**
 * Folds each run of two or more identical machine writes into one line. The scheduler writes a
 * job's next run back after every run, so a subscription's history was otherwise "Modified, Receive
 * on, #scheduled-job" four times over, pushing what people did off the bottom of the card.
 */
export function collapseMachineRuns(entries: TrailEntry[]): (TrailEntry | MachineRun)[] {
  const out: (TrailEntry | MachineRun)[] = [];
  for (let i = 0; i < entries.length; ) {
    let j = i + 1;
    while (j < entries.length && sameRun(entries[i], entries[j])) j++;
    out.push(j - i > 1 ? { kind: "run", id: entries[i].id, entries: entries.slice(i, j) } : entries[i]);
    i = j;
  }
  return out;
}

export const isRun = (row: TrailEntry | MachineRun): row is MachineRun => "kind" in row && row.kind === "run";
