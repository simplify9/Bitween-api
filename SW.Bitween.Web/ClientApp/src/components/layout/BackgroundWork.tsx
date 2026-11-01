import { CircleAlert, CircleCheck, Hourglass, LoaderCircle, X } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { useLocation, useNavigate } from "react-router";
import {
  cancelBackground,
  dismissBackground,
  isLeftToHeader,
  isRunning,
  openBackground,
  type BackgroundRequest,
} from "../../api/http/background";
import { useBackground } from "../../lib/useBackground";
import { api } from "../../api";
import { followDownload } from "../../lib/saveFile";
import { Popover } from "../ui/Popover";

/*
 * Work carried on in the background (see api/http/background): what's running and what's ready, in
 * the header, and a notice when one finishes while the person is on another page.
 */


/** Why work ended without an answer, as the header says it. */
const ENDED: Record<string, string> = {
  Cancelled: "Cancelled.",
  TimedOut: "Stopped: it went 10 minutes without getting further.",
  Stopped: "The server restarted before it finished. Start it again.",
};

/** An export's answer is the zip, which "Download" saves again rather than going back to the page. */
const isExport = (r: BackgroundRequest) => r.path.startsWith("POST /xchanges/export");

/** A bring's answer is what it brought, which "Open" shows on its page: `?brought=` its id. */
const isBring = (r: BackgroundRequest) => r.path.startsWith("POST /mirror/bring");

/** `page`, showing what the bring `id` brought, rather than the picker it was asked from. */
function showingBrought(page: string, id: string): string {
  const url = new URL(page, window.location.origin);
  url.searchParams.delete("bring");
  url.searchParams.set("brought", id);
  return url.pathname + url.search;
}

/** What a line says under its name. */
function said(r: BackgroundRequest, now: number): { text: string; title: string } {
  const counted = r.total != null ? ` · ${(r.done ?? 0).toLocaleString()} of ${r.total.toLocaleString()}` : "";
  if (r.status === "Waiting") return { text: `Waiting for a free place · ${duration(r.startedOn, now)}`, title: "How long it has waited" };
  if (r.status === "Running") return { text: `Running${counted} · ${duration(r.startedOn, now)}`, title: "How far it has got, and for how long" };
  const took = `took ${duration(r.startedOn, Date.parse(r.finishedOn ?? r.startedOn))}`;
  if (r.status === "Done") return { text: r.summary ? `Ready · ${r.summary}` : `Ready · ${took}`, title: `It ${took}` };
  return { text: r.summary ? `${ENDED[r.status] ?? "Ended."} ${r.summary}` : (ENDED[r.status] ?? "Ended."), title: "" };
}

/** Now, every second while `ticking`. */
function useNow(ticking: boolean): number {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    if (!ticking) return;
    const timer = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(timer);
  }, [ticking]);
  return now;
}

function duration(from: string, to: number): string {
  const seconds = Math.max(0, Math.round((to - Date.parse(from)) / 1000));
  return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, "0")}`;
}

/** Goes back to what it was for: its page - or, for an export, its zip again; for a bring, what it brought. */
async function openWork(r: BackgroundRequest, navigate: (to: string) => void): Promise<void> {
  if (isExport(r) && r.status === "Done") {
    followDownload((await api.linkToBackgroundFile(r.id)).url);
    dismissBackground([r.id]);
    return;
  }
  if (isBring(r) && r.status === "Done") {
    dismissBackground([r.id]);
    navigate(showingBrought(r.page ?? window.location.pathname, r.id));
    return;
  }
  openBackground([r]);
  if (r.page) navigate(r.page);
}

/** The header's place for background work: nothing when there is none. */
export function BackgroundTray() {
  const { requests } = useBackground();
  const navigate = useNavigate();
  const running = requests.filter((r) => isRunning(r.status)).length;
  const ready = requests.filter((r) => r.status === "Done").length;
  const now = useNow(running > 0);
  if (requests.length === 0) return null;

  const summary = running ? `${running} running` : ready ? `${ready} ready` : `${requests.length} ended`;
  return (
    <Popover
      label={`Background work: ${summary}`}
      title={
        running
          ? "Work carrying on in the background. Keep working: each says here when it's ready."
          : "Work that finished in the background."
      }
      buttonClassName="flex h-8 shrink-0 cursor-pointer items-center gap-1.5 rounded-lg border border-ink-200 bg-white px-2.5 text-[13px] text-ink-600 hover:border-ink-300"
      width="w-80"
      closeOnScroll={false}
      button={
        <>
          {running ? (
            <LoaderCircle className="size-3.5 animate-spin text-ink-500" aria-hidden />
          ) : ready ? (
            <CircleCheck className="size-3.5 text-ok-600" aria-hidden />
          ) : (
            <CircleAlert className="size-3.5 text-warn-700" aria-hidden />
          )}
          <span>{summary}</span>
        </>
      }
    >
      {(close) => (
        <ul className="divide-y divide-ink-100" aria-label="Background work">
          {requests.map((r) => {
            const line = said(r, now);
            return (
              <li key={r.id} className="flex items-start gap-2 px-1.5 py-2 text-[13px]">
                <LineIcon request={r} />
                <div className="min-w-0 flex-1">
                  <div className="truncate font-medium text-ink-800" title={r.page ?? undefined}>
                    {r.label ?? "Background work"}
                  </div>
                  <div className="text-xs text-ink-500" title={line.title || undefined}>
                    {line.text}
                  </div>
                </div>
                {isRunning(r.status) ? (
                  <button
                    type="button"
                    onClick={() => void cancelBackground([r.id])}
                    title="Stop it. Work done in steps, like a bulk retry, stops after the step it's on and keeps what it did."
                    className="shrink-0 cursor-pointer rounded px-1.5 py-0.5 text-xs font-medium text-ink-600 hover:bg-ink-100"
                  >
                    Cancel
                  </button>
                ) : (
                  <>
                    <button
                      type="button"
                      onClick={() => {
                        close();
                        void openWork(r, navigate);
                      }}
                      title={isExport(r) && r.status === "Done" ? "Save the zip" : "Go back to the page it was for"}
                      className="shrink-0 cursor-pointer rounded px-1.5 py-0.5 text-xs font-medium text-crimson-700 hover:bg-crimson-50"
                    >
                      {isExport(r) && r.status === "Done" ? "Download" : "Open"}
                    </button>
                    <button
                      type="button"
                      onClick={() => dismissBackground([r.id])}
                      aria-label={`Remove ${r.label ?? "it"} from the list`}
                      title="Remove from the list"
                      className="shrink-0 cursor-pointer rounded p-0.5 text-ink-400 hover:bg-ink-100 hover:text-ink-700"
                    >
                      <X className="size-3.5" aria-hidden />
                    </button>
                  </>
                )}
              </li>
            );
          })}
        </ul>
      )}
    </Popover>
  );
}

function LineIcon({ request: r }: { request: BackgroundRequest }) {
  const className = "mt-0.5 size-4 shrink-0";
  if (r.status === "Waiting") return <Hourglass className={`${className} text-ink-400`} aria-label="Waiting" />;
  if (r.status === "Running") return <LoaderCircle className={`${className} animate-spin text-ink-400`} aria-label="Running" />;
  if (r.status === "Done") return <CircleCheck className={`${className} text-ok-600`} aria-label="Ready" />;
  return <CircleAlert className={`${className} text-warn-700`} aria-label="Ended" />;
}

/**
 * Says so when work finishes while the person is on another page, with the way back to it. Finishing
 * on the page that asked needs no notice - the page simply fills in - so that work leaves the list.
 */
export function BackgroundReadyNotice() {
  const { requests } = useBackground();
  const { pathname, search } = useLocation();
  const navigate = useNavigate();
  const here = pathname + search;
  // Only work this tab saw running: what the list brings back already over - after a reload, or
  // from another tab - was there to see in the header, and isn't news.
  const seenRunning = useRef(new Set<string>());
  const [notice, setNotice] = useState<BackgroundRequest | null>(null);

  useEffect(() => {
    for (const r of requests) {
      if (isRunning(r.status)) {
        seenRunning.current.add(r.id);
        continue;
      }
      if (!seenRunning.current.delete(r.id)) continue;
      // Its page shows it - unless its window closed first, as a bring's can.
      if (r.page === here && !isLeftToHeader(r.id)) dismissBackground([r.id]);
      else setNotice(r);
    }
  }, [requests, here]);

  useEffect(() => {
    if (!notice) return;
    const timer = setTimeout(() => setNotice(null), 15_000);
    return () => clearTimeout(timer);
  }, [notice]);

  if (!notice) return null;
  const ready = notice.status === "Done";
  return (
    <div
      role="status"
      className="fixed right-4 bottom-4 z-40 flex max-w-sm items-center gap-2 rounded-xl border border-ink-100 bg-white px-3 py-2 text-[13px] shadow-lg"
    >
      {ready ? (
        <CircleCheck className="size-4 shrink-0 text-ok-600" aria-hidden />
      ) : (
        <CircleAlert className="size-4 shrink-0 text-warn-700" aria-hidden />
      )}
      <span className="min-w-0 flex-1 truncate" title={notice.summary ?? undefined}>
        <span className="font-medium text-ink-800">{notice.label ?? "Background work"}</span>{" "}
        <span className="text-ink-500">{ready ? "is ready" : notice.status === "Cancelled" ? "was cancelled" : "ended without an answer"}</span>
      </span>
      <button
        type="button"
        onClick={() => {
          setNotice(null);
          void openWork(notice, navigate);
        }}
        title={isExport(notice) && ready ? "Save the zip" : "Go back to the page it was for"}
        className="shrink-0 cursor-pointer rounded px-1.5 py-0.5 text-xs font-medium text-crimson-700 hover:bg-crimson-50"
      >
        {isExport(notice) && ready ? "Download" : "Open"}
      </button>
      <button
        type="button"
        onClick={() => setNotice(null)}
        aria-label="Close"
        title="Close. It stays in the header until you open or remove it."
        className="shrink-0 cursor-pointer rounded p-0.5 text-ink-400 hover:bg-ink-100 hover:text-ink-700"
      >
        <X className="size-3.5" aria-hidden />
      </button>
    </div>
  );
}
