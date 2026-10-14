/** The top of an axis: the next round number (1, 2 or 5 times a power of ten) at or above the largest value. */
export function niceMax(max: number): number {
  if (max <= 0) return 1;
  const power = 10 ** Math.floor(Math.log10(max));
  for (const step of [1, 2, 5, 10]) if (step * power >= max) return step * power;
  return 10 * power;
}

/**
 * Whether a pointer event is someone moving over the chart, rather than the page appearing
 * under a mouse that was already resting there. The browser reports that as the pointer
 * entering (and, in some, as a move that went nowhere), which pinned a tooltip to one bar on
 * first paint before anyone had touched the chart. A touch or pen always counts.
 */
export function handMoved(e: { pointerType: string; movementX: number; movementY: number }): boolean {
  return e.pointerType !== "mouse" || e.movementX !== 0 || e.movementY !== 0;
}
