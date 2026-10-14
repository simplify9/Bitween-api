import type { ReactNode } from "react";
import { Monitor } from "lucide-react";
import { EDITOR_MIN_WIDTH } from "../../lib/useMediaQuery";
import { BackLink } from "./BackLink";
import { RowCard, type CardFact } from "./RowCard";

/**
 * The line a canvas or heavy editor leaves in its place on a phone. Squeezed to 390px, the
 * route canvas showed an empty grid with its one node off to the right, which reads as broken.
 * Saying where the editor went is better than a screen that looks like it failed to load: what a
 * phone is for (status, pause, the last error) stays on the page, and building waits for a desk.
 */
export function WiderScreenNote({ what }: { what: string }) {
  return (
    <p className="flex items-start gap-2 text-[13px] text-ink-500">
      <Monitor className="mt-0.5 size-4 shrink-0" aria-hidden />
      <span>
        {what} opens on a screen wider than {EDITOR_MIN_WIDTH} px.
      </span>
    </p>
  );
}

/**
 * A whole editor's stand-in on a phone: the thing being edited, a few facts about it, the note
 * above, and the way back. For editors with nothing an operator does from a phone - a mapping,
 * an adapter's code - so the card only has to say what this is and where it went.
 */
export function DeskOnlyCard({
  title,
  what,
  status,
  technical,
  facts,
  back,
  children,
}: {
  title: ReactNode;
  /** The editor, as the subject of "... opens on a screen wider than 900 px." */
  what: string;
  status?: ReactNode;
  technical?: ReactNode;
  facts?: CardFact[];
  back: { to: string; label: string };
  /** Actions under the card. */
  children?: ReactNode;
}) {
  return (
    <div className="space-y-3">
      <BackLink to={back.to} label={back.label} className="" />
      <div className="rounded-xl border border-ink-200 bg-white">
        <RowCard spec={{ title, status, technical, facts }} />
      </div>
      <WiderScreenNote what={what} />
      {children}
    </div>
  );
}
