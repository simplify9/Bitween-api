import { useEffect, useRef } from "react";
import { Link } from "react-router";
import { ChevronDown, ChevronUp, X } from "lucide-react";
import type { ExchangeRow } from "../../api";
import { Badge } from "../../components/ui/basics";
import { PromotedProps } from "../../components/config/shared";
import { timeUntil } from "../../lib/dates";
import { ExchangeDrawer } from "./ExchangeDrawer";
import { StatusBadge } from "./shared";

/**
 * The open exchange, beside the list rather than inside it. Opening a row used to push every
 * row under it down by the height of its documents; beside the list, the place you were in it
 * stays put, and the next exchange is one key (or one button) away.
 *
 * On a narrow window there is no "beside", so it comes in over the list from the right instead.
 */
export function ExchangeDetailPanel({
  x,
  onClose,
  onPrevious,
  onNext,
}: {
  x: ExchangeRow;
  onClose: () => void;
  onPrevious?: () => void;
  onNext?: () => void;
}) {
  const ref = useRef<HTMLElement>(null);

  // A new exchange starts at the top of the panel, not wherever the last one was scrolled to.
  useEffect(() => {
    ref.current?.scrollTo?.({ top: 0 });
  }, [x.id]);

  const navButton = "inline-flex size-8 items-center justify-center rounded-lg text-ink-600 hover:bg-ink-100 disabled:text-ink-300 disabled:hover:bg-transparent";

  return (
    <>
      {/* The backdrop only exists where the panel covers the list. */}
      <div className="fixed inset-0 z-30 bg-ink-950/30 xl:hidden" onClick={onClose} aria-hidden />
      <aside
        ref={ref}
        aria-label={`Exchange ${x.id}`}
        className="fixed inset-y-0 right-0 z-40 flex w-full max-w-xl flex-col overflow-y-auto border-l border-ink-200 bg-white shadow-xl xl:sticky xl:top-4 xl:z-auto xl:max-h-[calc(100vh-2rem)] xl:w-[560px] xl:max-w-none xl:shrink-0 xl:rounded-xl xl:border xl:shadow-none"
      >
        <div className="sticky top-0 z-10 flex flex-col gap-2.5 border-b border-ink-100 bg-white px-4 py-3">
          <div className="flex items-start justify-between gap-2">
            <div className="flex min-w-0 flex-wrap items-center gap-x-2 gap-y-1">
              <StatusBadge status={x.status} />
              {x.retryFor && <Badge tone="neutral">Retry</Badge>}
              {x.scheduledRetryOn && <Badge tone="warn">Auto-retry {timeUntil(x.scheduledRetryOn)}</Badge>}
              {x.aggregationXchangeId && <Badge tone="neutral">Aggregated</Badge>}
              <span className="min-w-0 text-[13px] text-ink-500">
                {x.subscriptionName ? (
                  <Link to={`/subscriptions/${x.subscriptionId}`} className="font-semibold text-ink-900 hover:link">
                    {x.subscriptionName}
                  </Link>
                ) : (
                  <span>No subscription</span>
                )}
                {x.partnerName && (
                  <>
                    {" · "}
                    <Link to={`/partners/${x.partnerId}`} className="text-ink-700 hover:link">
                      {x.partnerName}
                    </Link>
                  </>
                )}
                {" · "}
                <Link
                  to={`/information-types/${x.informationTypeId}`}
                  className="font-mono text-xs text-ink-700 hover:link"
                >
                  {x.informationTypeCode}
                </Link>
              </span>
            </div>
            <div className="flex shrink-0 items-center">
              <button type="button" className={navButton} onClick={onPrevious} disabled={!onPrevious} aria-label="Previous exchange" title="Previous exchange (K)">
                <ChevronUp className="size-4" aria-hidden />
              </button>
              <button type="button" className={navButton} onClick={onNext} disabled={!onNext} aria-label="Next exchange" title="Next exchange (J)">
                <ChevronDown className="size-4" aria-hidden />
              </button>
              <button type="button" className={navButton} onClick={onClose} aria-label="Close exchange" title="Close (Esc)">
                <X className="size-4" aria-hidden />
              </button>
            </div>
          </div>
          <PromotedProps properties={x.promotedProperties} max={6} fallbackId={x.id} />
        </div>
        <div className="px-4 py-4">
          <ExchangeDrawer key={x.id} x={x} />
        </div>
      </aside>
    </>
  );
}
