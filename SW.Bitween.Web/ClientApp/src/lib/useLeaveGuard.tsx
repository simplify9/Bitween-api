import { useCallback, useEffect, useRef } from "react";
import { useBlocker, useLocation } from "react-router";
import { Button } from "../components/ui/basics";
import { Dialog } from "../components/ui/overlays";

/**
 * Asks before an editor with unsaved changes is left: for a link or Back inside the app, with a
 * dialog; for a reload, a closed tab or a typed address, with the browser's own prompt.
 *
 * Only leaving the page counts. A change of query string (a tab, a selected route) stays on it,
 * and the editor's state with it.
 *
 * The page's own navigations after it has dealt with the changes — saved them, deleted the thing,
 * or confirmed in a dialog of its own — go through `leave`, so nobody is asked twice. A
 * destination that takes the changes along with it is let through by `carriedTo`.
 */
export function useLeaveGuard(dirty: boolean, { carriedTo }: { carriedTo?: (pathname: string) => boolean } = {}) {
  const leaving = useRef(false);
  const location = useLocation();
  useEffect(() => {
    leaving.current = false;
  }, [location.key]);

  const blocker = useBlocker(
    ({ currentLocation, nextLocation }) =>
      dirty &&
      !leaving.current &&
      currentLocation.pathname !== nextLocation.pathname &&
      !carriedTo?.(nextLocation.pathname),
  );

  useEffect(() => {
    if (!dirty) return;
    const warn = (e: BeforeUnloadEvent) => e.preventDefault();
    window.addEventListener("beforeunload", warn);
    return () => window.removeEventListener("beforeunload", warn);
  }, [dirty]);

  const leave = useCallback((go: () => void) => {
    leaving.current = true;
    go();
  }, []);

  const dialog =
    blocker.state === "blocked" ? (
      <Dialog title="Leave without saving?" onClose={() => blocker.reset()}>
        <div className="space-y-4">
          <p className="text-sm text-ink-600">Your changes on this page haven't been saved. Leaving drops them.</p>
          <div className="flex justify-end gap-2">
            <Button onClick={() => blocker.reset()}>Stay on this page</Button>
            <Button variant="danger" onClick={() => blocker.proceed()}>
              Leave without saving
            </Button>
          </div>
        </div>
      </Dialog>
    ) : null;

  return { leave, dialog };
}
