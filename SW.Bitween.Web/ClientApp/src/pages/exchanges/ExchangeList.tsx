import { useEffect, useState } from "react";
import { Link } from "react-router";
import { useQueryClient } from "@tanstack/react-query";
import { Loader2 } from "lucide-react";
import type { ExchangeRow } from "../../api";
import { Can } from "../../auth/guards";
import { Badge } from "../../components/ui/basics";
import { ExceptionLine } from "../../components/ui/Exception";
import { PromotedProps } from "../../components/config/shared";
import { clickableRow, clickableRowClass } from "../../components/ui/clickableRow";
import { duration, formatDateTime, timeAgo, timeUntil } from "../../lib/dates";
import { hasRetryChain, retryTreeQuery } from "./retryTree";
import { journeyStages, journeySummary } from "./journey";
import { JourneyStrip, StatusBadge } from "./shared";

/**
 * Where an exchange got to: the three-segment strip and the phrase under it. While a stage is
 * underway the phrase gets a spinner, so "Mapping now" reads as happening rather than as a label.
 */
function PipelineCell({ x }: { x: ExchangeRow }) {
  const running = journeyStages(x).some((s) => s.state === "running");
  return (
    <span className="flex flex-col gap-1">
      <JourneyStrip x={x} />
      <span className={`inline-flex items-center gap-1 text-[11px] whitespace-nowrap ${running ? "font-medium text-ink-700" : "text-ink-500"}`}>
        {running && <Loader2 className="size-3 animate-spin text-focus-500" aria-hidden />}
        {journeySummary(x)}
      </span>
    </span>
  );
}

/** Whether the window is narrow enough for cards. Off where it can't be asked, as in tests. */
function useNarrow(): boolean {
  const query = "(max-width: 767px)";
  const [narrow, setNarrow] = useState(() => typeof window.matchMedia === "function" && window.matchMedia(query).matches);
  useEffect(() => {
    if (typeof window.matchMedia !== "function") return;
    const mq = window.matchMedia(query);
    const on = () => setNarrow(mq.matches);
    mq.addEventListener?.("change", on);
    return () => mq.removeEventListener?.("change", on);
  }, []);
  return narrow;
}

export interface ListSelection {
  isSelected: (id: string) => boolean;
  toggle: (id: string) => void;
  allOnPage: boolean;
  toggleAllOnPage: () => void;
}

export function ExchangeList({
  rows,
  openId,
  onOpen,
  compact,
  selection,
}: {
  rows: ExchangeRow[];
  openId: string | null;
  /** Opens the row, or closes it when it is the open one. */
  onOpen: (id: string) => void;
  /** The detail panel is beside the list: fewer, narrower columns. */
  compact: boolean;
  selection: ListSelection;
}) {
  const queryClient = useQueryClient();
  const narrow = useNarrow();

  /* Hovering a row is most of a second's head start on opening it, which is enough that its
     retry chain is already there when the panel renders. Only for rows that have one - most
     exchanges do not. */
  const prefetch = (x: ExchangeRow) => {
    if (hasRetryChain(x)) void queryClient.prefetchQuery(retryTreeQuery(x.id));
  };

  const markers = (x: ExchangeRow) => (
    <>
      {x.retryFor && <Badge tone="neutral">Retry</Badge>}
      {x.scheduledRetryOn && <Badge tone="warn">Auto-retry {timeUntil(x.scheduledRetryOn)}</Badge>}
      {x.aggregationXchangeId && <Badge tone="neutral">Aggregated</Badge>}
    </>
  );

  const errorTone = (x: ExchangeRow) => (x.status === "badResponse" ? "text-warn-700" : "text-danger-700");

  if (narrow)
    return (
      <ul className="flex flex-col gap-2.5" aria-label="Exchanges">
        {rows.map((x) => (
          <li key={x.id} data-row-id={x.id}>
            <div
              {...clickableRow(() => onOpen(x.id))}
              className={`flex cursor-pointer flex-col gap-2 rounded-xl border bg-white px-3.5 py-3 ${
                openId === x.id ? "border-crimson-300" : "border-ink-100"
              } ${clickableRowClass}`}
            >
              <div className="flex items-center justify-between gap-2">
                <span className="flex flex-wrap items-center gap-1.5">
                  <Can permission="exchanges.view">
                    <input
                      type="checkbox"
                      aria-label={`Select ${x.id}`}
                      className="size-4 cursor-pointer accent-crimson-600"
                      checked={selection.isSelected(x.id)}
                      onClick={(e) => e.stopPropagation()}
                      onChange={() => selection.toggle(x.id)}
                    />
                  </Can>
                  <StatusBadge status={x.status} />
                  {markers(x)}
                </span>
                <span className="text-xs whitespace-nowrap text-ink-500" title={formatDateTime(x.startedOn)}>
                  {timeAgo(x.startedOn)}
                </span>
              </div>
              <PromotedProps properties={x.promotedProperties} fallbackId={x.id} />
              <span className="text-[13px] text-ink-600">
                {x.subscriptionName ?? "No subscription"}
                {x.partnerName && <span className="text-ink-500"> · {x.partnerName}</span>}
              </span>
              {x.exception && <ExceptionLine text={x.exception} icon className={`font-mono text-[11.5px] ${errorTone(x)}`} />}
            </div>
          </li>
        ))}
      </ul>
    );

  const cell = "px-3 py-2.5 align-top";

  return (
    <div className="overflow-x-auto">
      <table className="w-full text-left text-sm">
        <thead>
          <tr className="border-b border-ink-100 text-[11px] font-medium tracking-wide text-ink-500 uppercase">
            <th className="w-10 py-2.5 pl-4">
              <Can permission="exchanges.view">
                <input
                  type="checkbox"
                  aria-label="Select all on this page"
                  className="size-3.5 cursor-pointer accent-crimson-600"
                  checked={selection.allOnPage}
                  onChange={selection.toggleAllOnPage}
                />
              </Can>
            </th>
            <th className="px-3 py-2 font-medium">Exchange</th>
            <th className="px-3 py-2 font-medium">{compact ? "Status and pipeline" : "Status"}</th>
            {!compact && <th className="px-3 py-2 font-medium">Pipeline</th>}
            {!compact && <th className="px-3 py-2 font-medium">Route</th>}
            <th className="py-2 pr-4 pl-3 text-right font-medium">Started</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((x) => {
            const isOpen = openId === x.id;
            return (
              <tr
                key={x.id}
                data-row-id={x.id}
                {...clickableRow(() => onOpen(x.id))}
                aria-expanded={isOpen}
                onMouseEnter={() => prefetch(x)}
                className={`cursor-pointer border-b border-ink-50 transition-colors last:border-0 ${
                  isOpen ? "bg-crimson-50/70 shadow-[inset_3px_0_0_var(--color-crimson-600)]" : "hover:bg-ink-50/60"
                } ${clickableRowClass}`}
              >
                <td className="py-2.5 pl-4 align-top" onClick={(e) => e.stopPropagation()}>
                  <Can permission="exchanges.view">
                    <input
                      type="checkbox"
                      aria-label={`Select ${x.id}`}
                      className="mt-1 size-3.5 cursor-pointer accent-crimson-600"
                      checked={selection.isSelected(x.id)}
                      onChange={() => selection.toggle(x.id)}
                    />
                  </Can>
                </td>
                <td className={`${cell} max-w-[28rem]`}>
                  <div className="flex min-w-0 flex-col gap-1">
                    <PromotedProps properties={x.promotedProperties} fallbackId={x.id} />
                    <span className="flex min-w-0 flex-wrap items-center gap-x-1.5 text-xs text-ink-500">
                      {/* Only the links swallow the click, never the cells: an opted-out cell would
                          leave part of a clickable row doing nothing when clicked. */}
                      <Link
                        to={`/information-types/${x.informationTypeId}`}
                        onClick={(e) => e.stopPropagation()}
                        className="font-mono text-[11px] text-ink-600 hover:link"
                      >
                        {x.informationTypeCode}
                      </Link>
                      {/* Beside the open exchange there is no Route column, so the route rides
                          along here instead of disappearing. */}
                      {compact && (
                        <span className="min-w-0 truncate">
                          · {x.subscriptionName ?? "No subscription"}
                          {x.partnerName && <span className="text-ink-400"> · {x.partnerName}</span>}
                        </span>
                      )}
                    </span>
                    {x.exception && (
                      <ExceptionLine text={x.exception} icon className={`max-w-full font-mono text-[11.5px] ${errorTone(x)}`} />
                    )}
                  </div>
                </td>
                <td className={cell}>
                  {/* Status and the relationship markers read as one thought:
                      "failed, and a retry is already booked". */}
                  <span className="flex flex-wrap items-center gap-1">
                    <StatusBadge status={x.status} />
                    {markers(x)}
                  </span>
                  {/* And the pipeline, under the status it explains. */}
                  {compact && (
                    <span className="mt-1.5 block">
                      <PipelineCell x={x} />
                    </span>
                  )}
                </td>
                {!compact && (
                  <td className={cell}>
                    <PipelineCell x={x} />
                  </td>
                )}
                {!compact && (
                  <td className={`${cell} text-[13px] whitespace-nowrap`}>
                    <span className="flex flex-col gap-0.5">
                      {x.subscriptionName ? (
                        <Link
                          to={`/subscriptions/${x.subscriptionId}`}
                          onClick={(e) => e.stopPropagation()}
                          className="font-medium text-ink-800 hover:link"
                        >
                          {x.subscriptionName}
                        </Link>
                      ) : (
                        <span className="text-ink-500">-</span>
                      )}
                      {x.partnerName && (
                        <Link
                          to={`/partners/${x.partnerId}`}
                          onClick={(e) => e.stopPropagation()}
                          className="text-xs text-ink-500 hover:link"
                        >
                          {x.partnerName}
                        </Link>
                      )}
                    </span>
                  </td>
                )}
                <td className="py-2.5 pr-4 pl-3 text-right align-top whitespace-nowrap">
                  <span className="flex flex-col items-end gap-0.5">
                    <span className="text-ink-700 tabular-nums" title={formatDateTime(x.startedOn)}>
                      {timeAgo(x.startedOn)}
                    </span>
                    <span className="text-xs text-ink-500 tabular-nums">
                      {x.finishedOn ? duration(x.startedOn, x.finishedOn) : "Running"}
                    </span>
                  </span>
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
