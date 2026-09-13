import type { ReactNode } from "react";

/**
 * One labelled cell of the facts strip.
 *
 * Its own file so `LaneAndRetry` and `Overview` can both use it without either importing
 * the other — `Overview` pulls in the whole run history, which a create page has no use for.
 */
export function Fact({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="min-w-0">
      <p className="mb-1 text-[11px] font-medium tracking-wide text-ink-400 uppercase">{label}</p>
      <div className="text-[13px] text-ink-800">{children}</div>
    </div>
  );
}
