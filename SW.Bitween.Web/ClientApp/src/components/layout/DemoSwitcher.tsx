import { useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router";
import { useQuery } from "@tanstack/react-query";
import { Check, RotateCcw, VenetianMask } from "lucide-react";
import { api } from "../../api";
import { useSession } from "../../auth/SessionContext";
import { homePath } from "../../nav";
import { Avatar } from "../ui/Avatar";

/**
 * Prototype-only: jump between demo people to see how their roles
 * change the app. Styled as scaffolding (dashed) so it never reads
 * as part of the product.
 */
export function DemoSwitcher() {
  const { session, adoptSession } = useSession();
  const navigate = useNavigate();
  const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const ref = useRef<HTMLDivElement>(null);

  const personas = useQuery({
    queryKey: ["demo-personas"],
    queryFn: () => api.demo.listPersonas(),
    enabled: open,
  });

  useEffect(() => {
    if (!open) return;
    const onDown = (e: MouseEvent) => {
      if (!ref.current?.contains(e.target as Node)) setOpen(false);
    };
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && setOpen(false);
    document.addEventListener("mousedown", onDown);
    document.addEventListener("keydown", onKey);
    return () => {
      document.removeEventListener("mousedown", onDown);
      document.removeEventListener("keydown", onKey);
    };
  }, [open]);

  if (!session) return null;

  const switchTo = async (userId: string) => {
    if (userId === session.user.id) return setOpen(false);
    setBusy(true);
    try {
      const next = await api.demo.switchTo(userId);
      adoptSession(next);
      setOpen(false);
      navigate(homePath(next));
    } finally {
      setBusy(false);
    }
  };

  const reset = async () => {
    setBusy(true);
    try {
      await api.demo.reset();
      const next = await api.getSession();
      if (next) {
        adoptSession(next);
        setOpen(false);
        navigate(homePath(next));
      } else {
        location.assign(import.meta.env.BASE_URL);
      }
    } finally {
      setBusy(false);
    }
  };

  return (
    <div ref={ref} className="fixed right-4 bottom-4 z-40">
      {open && (
        <div className="absolute right-0 bottom-12 w-72 rounded-2xl border border-dashed border-ink-300 bg-white p-2 shadow-xl">
          <p className="px-2 pt-1.5 pb-2 text-[11px] leading-snug text-ink-500">
            Prototype only — switch people to see how roles change what the app shows.
          </p>
          <ul className="space-y-0.5">
            {(personas.data ?? [])
              .filter((u) => u.status === "active")
              .map((u) => (
                <li key={u.id}>
                  <button
                    disabled={busy}
                    onClick={() => void switchTo(u.id)}
                    className="flex w-full items-center gap-2.5 rounded-xl px-2 py-1.5 text-left hover:bg-ink-50"
                  >
                    <Avatar name={u.displayName} size="sm" />
                    <span className="min-w-0 flex-1">
                      <span className="block truncate text-sm font-medium text-ink-800">
                        {u.displayName}
                      </span>
                    </span>
                    {u.id === session.user.id && <Check className="size-4 shrink-0 text-crimson-600" />}
                  </button>
                </li>
              ))}
          </ul>
          <div className="mt-1.5 border-t border-ink-100 pt-1.5">
            <button
              disabled={busy}
              onClick={() => void reset()}
              className="flex w-full items-center gap-2 rounded-xl px-2 py-1.5 text-left text-[13px] text-ink-600 hover:bg-ink-50"
            >
              <RotateCcw className="size-3.5" /> Reset demo data
            </button>
          </div>
        </div>
      )}

      <button
        onClick={() => setOpen((o) => !o)}
        className="flex items-center gap-2 rounded-full border border-dashed border-ink-400 bg-white py-1.5 pr-3.5 pl-2.5 text-[13px] font-medium text-ink-700 shadow-lg hover:border-ink-600"
      >
        <VenetianMask className="size-4 text-crimson-600" />
        Demo: {session.user.displayName.split(" ")[0]}
      </button>
    </div>
  );
}
