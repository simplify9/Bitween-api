import { type CSSProperties, type ReactNode, useEffect, useRef, useState } from "react";
import { Maximize2, Minus, Plus } from "lucide-react";

/**
 * A scrollable diagram surface: drag the background to pan, fixed steps to zoom.
 *
 * Pannable but never free-form — nodes are not dragged in either diagram that uses
 * this, because both layouts are derived from the configuration and rearranging them
 * could only produce pictures that disagree with it. What panning buys is real: a
 * response chain, or a topology of a dozen gateways, is wider than any screen.
 *
 * Shared by the bus-gateway studio and the flow map so the two feel like one tool.
 * The zoom steps, the keyboard shortcuts and the control cluster all have to agree
 * between them, and two copies of this would start disagreeing on the first edit.
 */

/** Fixed steps rather than free zoom: text has to stay readable to be worth drawing. */
const ZOOM_STEPS = [0.55, 0.7, 0.85, 1];

export function PanZoomCanvas({
  children,
  /** Changes when the subject changes — the view returns to the start of it. */
  resetKey,
  /** What the fit control offers, e.g. "Fit the whole path". Shown in its tooltip. */
  fitLabel,
  contentClassName = "",
  contentStyle,
}: {
  children: ReactNode;
  resetKey: string;
  fitLabel: string;
  contentClassName?: string;
  contentStyle?: CSSProperties;
}) {
  const viewport = useRef<HTMLDivElement>(null);
  const content = useRef<HTMLDivElement>(null);
  const [zoom, setZoom] = useState(1);

  // A new subject means a new diagram: start it at the beginning rather than
  // wherever the last one happened to be scrolled to.
  useEffect(() => {
    viewport.current?.scrollTo({ left: 0, top: 0 });
  }, [resetKey]);

  const step = (delta: number) =>
    setZoom((z) => {
      const i = ZOOM_STEPS.indexOf(z);
      return ZOOM_STEPS[
        Math.min(ZOOM_STEPS.length - 1, Math.max(0, (i < 0 ? ZOOM_STEPS.length - 1 : i) + delta))
      ];
    });

  /** The largest step whose content fits the viewport, so "everything at once" is one click. */
  const fit = () => {
    const box = viewport.current;
    const inner = content.current;
    if (!box || !inner) return;
    // Divided by the current zoom because scrollWidth is already scaled by it.
    const natural = inner.scrollWidth / zoom;
    const best = [...ZOOM_STEPS].reverse().find((z) => natural * z <= box.clientWidth) ?? ZOOM_STEPS[0];
    setZoom(best);
    box.scrollTo({ left: 0 });
  };

  // Drag the background to pan. Refused on controls: a drag that started on a
  // button or a link would either swallow the click or move what you were aiming at.
  const drag = useRef<{ x: number; y: number; left: number; top: number } | null>(null);
  const onPointerDown = (e: React.PointerEvent) => {
    if (e.button !== 0 && e.button !== 1) return;
    if ((e.target as HTMLElement).closest("button, a, input, select, textarea")) return;
    const box = viewport.current;
    if (!box) return;
    drag.current = { x: e.clientX, y: e.clientY, left: box.scrollLeft, top: box.scrollTop };
    box.setPointerCapture(e.pointerId);
  };
  const onPointerMove = (e: React.PointerEvent) => {
    const d = drag.current;
    const box = viewport.current;
    if (!d || !box) return;
    box.scrollLeft = d.left - (e.clientX - d.x);
    box.scrollTop = d.top - (e.clientY - d.y);
  };
  const endDrag = (e: React.PointerEvent) => {
    if (drag.current) viewport.current?.releasePointerCapture(e.pointerId);
    drag.current = null;
  };

  // Keyboard zoom, stood down while typing — an operator editing a handler URL
  // types "+" and "-" constantly.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.metaKey || e.ctrlKey || e.altKey) return;
      const el = document.activeElement;
      if (el instanceof HTMLElement && (el.isContentEditable || ["INPUT", "TEXTAREA", "SELECT"].includes(el.tagName)))
        return;
      if (e.key === "+" || e.key === "=") step(1);
      else if (e.key === "-" || e.key === "_") step(-1);
      else if (e.key === "0") fit();
      else return;
      e.preventDefault();
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [zoom]);

  return (
    <div className="relative min-h-0 flex-1">
      <div
        ref={viewport}
        onPointerDown={onPointerDown}
        onPointerMove={onPointerMove}
        onPointerUp={endDrag}
        onPointerCancel={endDrag}
        className={`h-full overflow-auto bg-canvas ${drag.current ? "cursor-grabbing" : "cursor-grab"}`}
        style={{
          backgroundImage: "radial-gradient(var(--color-ink-200) 1px, transparent 1px)",
          backgroundSize: "16px 16px",
        }}
      >
        {/*
          `zoom` rather than a transform: it reflows, so the scroll extents stay
          correct on their own and hit testing keeps working. A transform would need
          the wrapper's size compensated by hand and would still leave every node's
          click target in the wrong place.
        */}
        <div ref={content} className={contentClassName} style={{ ...contentStyle, zoom }}>
          {children}
        </div>
      </div>

      {/* Bottom-right, out of the diagram's way and out of any floating panel's. */}
      <div className="absolute right-3 bottom-3 flex items-center gap-0.5 rounded-xl border border-ink-200 bg-white/95 p-1 shadow-sm backdrop-blur">
        <button
          type="button"
          onClick={() => step(-1)}
          disabled={zoom === ZOOM_STEPS[0]}
          title="Zoom out  −"
          aria-label="Zoom out"
          className="rounded-lg p-1.5 text-ink-500 hover:bg-ink-100 hover:text-ink-800 disabled:opacity-30"
        >
          <Minus className="size-3.5" />
        </button>
        <button
          type="button"
          onClick={fit}
          title={`${fitLabel}  0`}
          className="rounded-lg px-1.5 py-1 text-[11px] font-medium text-ink-500 tabular-nums hover:bg-ink-100 hover:text-ink-800"
        >
          {Math.round(zoom * 100)}%
        </button>
        <button
          type="button"
          onClick={() => step(1)}
          disabled={zoom === ZOOM_STEPS.at(-1)}
          title="Zoom in  +"
          aria-label="Zoom in"
          className="rounded-lg p-1.5 text-ink-500 hover:bg-ink-100 hover:text-ink-800 disabled:opacity-30"
        >
          <Plus className="size-3.5" />
        </button>
        <span className="mx-0.5 h-4 w-px bg-ink-200" aria-hidden />
        <button
          type="button"
          onClick={fit}
          title={`${fitLabel}  0`}
          aria-label={fitLabel}
          className="rounded-lg p-1.5 text-ink-500 hover:bg-ink-100 hover:text-ink-800"
        >
          <Maximize2 className="size-3.5" />
        </button>
      </div>
    </div>
  );
}
