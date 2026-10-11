import { useCallback, useState } from "react";

/**
 * A pane's width, dragged by a handle and remembered in this browser. `side` says which edge the
 * handle is on: a pane on the left grows as the handle moves right, one on the right as it moves left.
 */
export function useResizable(key: string, initial: number, min: number, max: number, side: "left" | "right") {
  const [width, setWidth] = useState(() => {
    try {
      const stored = Number(localStorage.getItem(key));
      return stored >= min && stored <= max ? stored : initial;
    } catch {
      return initial;
    }
  });

  const startDrag = useCallback(
    (e: React.PointerEvent) => {
      e.preventDefault();
      const startX = e.clientX;
      const startWidth = width;
      let last = startWidth;
      const move = (ev: PointerEvent) => {
        const delta = side === "left" ? ev.clientX - startX : startX - ev.clientX;
        last = Math.min(max, Math.max(min, startWidth + delta));
        setWidth(last);
      };
      const up = () => {
        window.removeEventListener("pointermove", move);
        window.removeEventListener("pointerup", up);
        document.body.style.cursor = "";
        try {
          localStorage.setItem(key, String(last));
        } catch {
          // Not remembered; the pane still has the width for now.
        }
      };
      document.body.style.cursor = "col-resize";
      window.addEventListener("pointermove", move);
      window.addEventListener("pointerup", up);
    },
    [width, key, min, max, side],
  );

  return { width, startDrag };
}
