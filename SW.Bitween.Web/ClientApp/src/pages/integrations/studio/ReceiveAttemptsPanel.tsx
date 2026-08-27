import { useState } from "react";
import { Link } from "react-router";
import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { Check, ChevronDown, ChevronRight, Copy } from "lucide-react";
import { api, type ReceiveAttemptRow, type ReceiveOutcome } from "../../../api";
import { Badge, EmptyState, LoadingBlock } from "../../../components/ui/basics";
import { Select } from "../../../components/ui/forms";
import { Pagination } from "../../../components/ui/Pagination";
import { Table } from "../../../components/ui/Table";
import { PromotedProps } from "../../../components/config/shared";
import { StatusBadge } from "../../exchanges/shared";
import { formatDateTime, timeAgo } from "../../../lib/dates";

const PAGE_SIZE = 25;

/**
 * Both scheduled types write into the same history, so this panel serves both — but a
 * receiver "checks for new data" and an aggregation "rolls up what its source produced",
 * and one set of words cannot honestly describe both. The outcomes underneath are the
 * same three; only what they mean to a person changes.
 */
export type AttemptKind = "receiving" | "aggregation";

const WORDING: Record<
  AttemptKind,
  {
    description: string;
    empty: string;
    emptyHint: string;
    exchangeHeader: string;
    exchangeHint: string;
    resultHint: string;
    failed: string;
    failedHint: string;
    nothing: string;
    nothingHint: string;
    outcomes: { value: string; label: string }[];
  }
> = {
  receiving: {
    description: "Every time this integration checked for new data — what it found, and what happened to it.",
    empty: "No checks recorded yet",
    emptyHint: "This integration hasn't checked for new data since this history started being kept.",
    exchangeHeader: "Exchange",
    exchangeHint: "The exchanges this run created, one per document it received.",
    resultHint: "What the run found when it checked its source.",
    failed: "Couldn't check",
    failedHint: "The run could not reach or read its source — no documents were received.",
    nothing: "Nothing new",
    nothingHint: "Checked for new data — there was nothing to receive this time.",
    outcomes: [
      { value: "", label: "All" },
      { value: "Failed", label: "Couldn't check" },
      { value: "NoNewData", label: "Nothing new" },
      { value: "Received", label: "Received data" },
    ],
  },
  aggregation: {
    description: "Every time this aggregation ran — whether it had anything to collect, and the exchange it produced.",
    empty: "No runs recorded yet",
    emptyHint: "This aggregation hasn't run since this history started being kept.",
    exchangeHeader: "Roll-up",
    exchangeHint: "The single exchange this run produced. Opening it also lists everything it collected.",
    resultHint: "Whether the run found anything outstanding to roll up.",
    failed: "Couldn't run",
    failedHint: "The run threw before it finished — nothing was rolled up.",
    nothing: "Nothing to roll up",
    nothingHint: "Ran, but the source had no new successful exchanges waiting. No exchange is created for an empty period.",
    outcomes: [
      { value: "", label: "All" },
      { value: "Failed", label: "Couldn't run" },
      { value: "NoNewData", label: "Nothing to roll up" },
      { value: "Received", label: "Rolled up" },
    ],
  },
};

function CopyErrorButton({ text }: { text: string }) {
  const [copied, setCopied] = useState(false);
  return (
    <button
      type="button"
      onClick={async (e) => {
        e.stopPropagation();
        await navigator.clipboard.writeText(text);
        setCopied(true);
        setTimeout(() => setCopied(false), 1400);
      }}
      title="Copy full error"
      className="shrink-0 rounded p-0.5 text-ink-400 hover:bg-ink-100 hover:text-ink-700"
    >
      {copied ? <Check className="size-3" /> : <Copy className="size-3" />}
    </button>
  );
}

function ErrorText({ text }: { text: string }) {
  const [expanded, setExpanded] = useState(false);
  return (
    <span className="flex items-start gap-1">
      <button
        type="button"
        onClick={(e) => {
          e.stopPropagation();
          setExpanded((v) => !v);
        }}
        aria-label={expanded ? "Collapse error" : "Expand error"}
        aria-expanded={expanded}
        className="mt-0.5 shrink-0 text-ink-400 hover:text-ink-700"
      >
        {expanded ? <ChevronDown className="size-3" /> : <ChevronRight className="size-3" />}
      </button>
      {expanded ? (
        <pre className="max-h-48 max-w-md overflow-auto font-mono text-[11px] whitespace-pre-wrap text-danger-700">
          {text}
        </pre>
      ) : (
        <span className="block max-w-xs truncate font-mono text-[11px] text-danger-700" title={text}>
          {text}
        </span>
      )}
      <CopyErrorButton text={text} />
    </span>
  );
}

function AttemptResult({ attempt, kind }: { attempt: ReceiveAttemptRow; kind: AttemptKind }) {
  const w = WORDING[kind];
  if (attempt.outcome === "Failed")
    return (
      <span className="flex flex-col gap-0.5">
        <Badge tone="danger" title={w.failedHint}>
          {w.failed}
        </Badge>
        {attempt.errorMessage && <ErrorText text={attempt.errorMessage} />}
      </span>
    );
  if (attempt.outcome === "NoNewData") return <Badge title={w.nothingHint}>{w.nothing}</Badge>;
  // No count for an aggregation: the attempt holds the one roll-up it made, not the number
  // of exchanges that went into it — saying "Rolled up 1" would be a plain lie. The link in
  // the next column opens the roll-up together with everything it collected.
  if (kind === "aggregation")
    return (
      <span className="text-ink-800" title="Collected everything outstanding into one exchange.">
        Rolled up
      </span>
    );
  return (
    <span className="text-ink-800">
      Received {attempt.exchanges.length} item{attempt.exchanges.length === 1 ? "" : "s"}
    </span>
  );
}

function AttemptExchanges({ exchanges }: { exchanges: ReceiveAttemptRow["exchanges"] }) {
  if (exchanges.length === 0) return <span className="text-ink-400">—</span>;
  return (
    <span className="flex flex-col gap-1">
      {exchanges.map((x) => (
        <Link
          key={x.id}
          to={`/exchanges?ids=${encodeURIComponent(x.id)}`}
          onClick={(e) => e.stopPropagation()}
          className="flex items-center gap-1.5 hover:opacity-70"
        >
          <PromotedProps properties={x.promotedProperties} fallbackId={x.id} />
          <StatusBadge status={x.status} />
        </Link>
      ))}
    </span>
  );
}

/**
 * Every time a Receiving integration checked for new data — replaces the old pairing of
 * the scheduler's own run history (Quartz vocabulary an operator has no reason to know,
 * and which always reports success even when the receive step itself failed) next to a
 * capped 8-row exchange glance. One row per run regardless of what it found, so a poll
 * that couldn't even connect shows up here just as much as one that produced an exchange —
 * "Exchanges" as a title would undersell the rows that have none.
 */
export function ReceiveAttemptsPanel({
  subscriptionId,
  kind,
}: {
  subscriptionId: number;
  kind: AttemptKind;
}) {
  const w = WORDING[kind];
  const [outcome, setOutcome] = useState<ReceiveOutcome | null>(null);
  const [offset, setOffset] = useState(0);

  const attempts = useQuery({
    queryKey: ["receive-attempts", subscriptionId, outcome, offset],
    queryFn: () => api.searchReceiveAttempts(subscriptionId, { outcome, offset, limit: PAGE_SIZE }),
    placeholderData: keepPreviousData,
  });

  const rows = attempts.data?.result ?? [];
  const total = attempts.data?.total ?? 0;

  return (
    <div>
      <div className="mb-3 flex items-start justify-between gap-3">
        <div>
          <h2 className="text-[15px] font-semibold text-ink-900">Runs</h2>
          <p className="mt-0.5 text-[13px] text-ink-500">{w.description}</p>
        </div>
        <div className="w-44 shrink-0">
          <Select
            aria-label="Filter by result"
            className="!h-8 text-[13px]"
            value={outcome ?? ""}
            onChange={(e) => {
              setOutcome((e.target.value || null) as ReceiveOutcome | null);
              setOffset(0);
            }}
            options={w.outcomes}
          />
        </div>
      </div>

      {attempts.isPending ? (
        <LoadingBlock label="Loading runs…" />
      ) : rows.length === 0 ? (
        <EmptyState title={outcome ? "Nothing matches" : w.empty}>
          {outcome ? "Try a different filter." : w.emptyHint}
        </EmptyState>
      ) : (
        <Table
          rows={rows}
          rowKey={(a) => a.id}
          minWidth="min-w-160"
          footer={
            <Pagination offset={offset} limit={PAGE_SIZE} total={total} onOffsetChange={setOffset} />
          }
          columns={[
            {
              header: "When",
              headerTitle: "When the run started. Hover a value for the exact time.",
              cell: (a) => (
                <span title={formatDateTime(a.startedOn)} className="text-ink-800">
                  {timeAgo(a.startedOn)}
                </span>
              ),
            },
            {
              header: "Result",
              headerTitle: w.resultHint,
              cell: (a) => <AttemptResult attempt={a} kind={kind} />,
            },
            {
              header: w.exchangeHeader,
              headerTitle: w.exchangeHint,
              cell: (a) => <AttemptExchanges exchanges={a.exchanges} />,
            },
          ]}
        />
      )}
    </div>
  );
}
