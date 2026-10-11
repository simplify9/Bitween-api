import { useCallback, useEffect, useRef, useState } from "react";

export interface Connection {
  id: string;
  /** Value of the source element's identifying attribute. */
  source: string;
  /** Value of the target element's identifying attribute. */
  target: string;
  /** Drawn heavier, for the one being hovered or edited. */
  emphasis?: boolean;
}

interface Line {
  id: string;
  d: string;
  emphasis: boolean;
}

/**
 * Curves joining two lists that scroll independently.
 *
 * Endpoints are found by a data attribute rather than by ref maps the panels have
 * to register into and keep in step - the DOM already knows where its own rows
 * are. The svg draws outside its own box on purpose, so it needs only a narrow
 * gutter to live in while the curves reach the rows on either side.
 */
export function ConnectionLines({
  connections,
  sourceAttribute,
  targetAttribute,
  watchSelector,
}: {
  connections: Connection[];
  sourceAttribute: string;
  targetAttribute: string;
  /** Elements whose scrolling or resizing moves the endpoints. */
  watchSelector: string;
}) {
  const svg = useRef<SVGSVGElement>(null);
  const frame = useRef(0);
  const [lines, setLines] = useState<Line[]>([]);

  const compute = useCallback(() => {
    const box = svg.current?.getBoundingClientRect();
    if (!box) return;

    const next: Line[] = [];
    const escape = (value: string) => value.replace(/["\\]/g, "\\$&");

    for (const c of connections) {
      const from = document.querySelector<HTMLElement>(`[${sourceAttribute}="${escape(c.source)}"]`);
      const to = document.querySelector<HTMLElement>(`[${targetAttribute}="${escape(c.target)}"]`);
      if (!from || !to) continue;

      const a = from.getBoundingClientRect();
      const b = to.getBoundingClientRect();

      // An endpoint scrolled out of its panel would otherwise be drawn against
      // the panel's edge, which reads as a line to the wrong row.
      const outside = (r: DOMRect) => r.bottom < box.top - 8 || r.top > box.bottom + 8;
      if (outside(a) || outside(b)) continue;

      const x1 = a.right - box.left;
      const y1 = a.top + a.height / 2 - box.top;
      const x2 = b.left - box.left;
      const y2 = b.top + b.height / 2 - box.top;
      const cx = (x1 + x2) / 2;

      next.push({
        id: c.id,
        d: `M ${x1} ${y1} C ${cx} ${y1} ${cx} ${y2} ${x2} ${y2}`,
        emphasis: c.emphasis ?? false,
      });
    }

    setLines(next);
  }, [connections, sourceAttribute, targetAttribute]);

  useEffect(() => {
    const schedule = () => {
      cancelAnimationFrame(frame.current);
      frame.current = requestAnimationFrame(compute);
    };

    schedule();

    const watched = [...document.querySelectorAll<HTMLElement>(watchSelector)];
    // Resizing catches a row being expanded, which moves everything below it
    // without anything scrolling.
    const observer = new ResizeObserver(schedule);
    for (const el of watched) {
      el.addEventListener("scroll", schedule, { passive: true });
      observer.observe(el);
      if (el.firstElementChild) observer.observe(el.firstElementChild);
    }
    window.addEventListener("resize", schedule, { passive: true });

    return () => {
      cancelAnimationFrame(frame.current);
      observer.disconnect();
      for (const el of watched) el.removeEventListener("scroll", schedule);
      window.removeEventListener("resize", schedule);
    };
  }, [compute, watchSelector]);

  return (
    <svg
      ref={svg}
      aria-hidden
      className="pointer-events-none absolute inset-0 h-full w-full overflow-visible"
    >
      {lines.map((line) => (
        <path
          key={line.id}
          d={line.d}
          fill="none"
          strokeWidth={line.emphasis ? 2 : 1}
          className={line.emphasis ? "stroke-crimson-500" : "stroke-ink-300"}
        />
      ))}
    </svg>
  );
}
