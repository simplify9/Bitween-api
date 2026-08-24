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

const OUTCOME_OPTIONS: { value: string; label: string }[] = [
  { value: "", label: "All" },
  { value: "Failed", label: "Couldn't check" },
  { value: "NoNewData", label: "Nothing new" },
  { value: "Received", label: "Received data" },
];

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

function AttemptResult({ attempt }: { attempt: ReceiveAttemptRow }) {
  if (attempt.outcome === "Failed")
    return (
      <span className="flex flex-col gap-0.5">
        <Badge tone="danger">Couldn't check</Badge>
        {attempt.errorMessage && <ErrorText text={attempt.errorMessage} />}
      </span>
    );
  if (attempt.outcome === "NoNewData")
    return <Badge title="Checked for new data — there was nothing to receive this time.">Nothing new</Badge>;
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
export function ReceiveAttemptsPanel({ subscriptionId }: { subscriptionId: number }) {
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
          <p className="mt-0.5 text-[13px] text-ink-500">
            Every time this integration checked for new data — what it found, and what happened to it.
          </p>
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
            options={OUTCOME_OPTIONS}
          />
        </div>
      </div>

      {attempts.isPending ? (
        <LoadingBlock label="Loading runs…" />
      ) : rows.length === 0 ? (
        <EmptyState title={outcome ? "Nothing matches" : "No checks recorded yet"}>
          {outcome
            ? "Try a different filter."
            : "This integration hasn't checked for new data since this history started being kept."}
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
              cell: (a) => (
                <span title={formatDateTime(a.startedOn)} className="text-ink-800">
                  {timeAgo(a.startedOn)}
                </span>
              ),
            },
            { header: "Result", cell: (a) => <AttemptResult attempt={a} /> },
            { header: "Exchange", cell: (a) => <AttemptExchanges exchanges={a.exchanges} /> },
          ]}
        />
      )}
    </div>
  );
}
