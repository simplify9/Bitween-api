import { useNavigate } from "react-router";
import { CalendarClock, ChevronRight, CornerDownLeft, Layers, Webhook, Workflow } from "lucide-react";
import type { ReactNode } from "react";
import { useSessionCan } from "../../auth/guards";
import { Dialog } from "../../components/ui/overlays";

type Way = { icon: ReactNode; title: string; body: string; to: string; permission: Parameters<typeof useSessionCan>[0] };

/**
 * A subscription is created where whatever starts it lives, so it is wired in the moment it exists.
 * This asks what starts it and goes there, instead of leaving "where do I create one?" unanswered.
 */
const WAYS: Way[] = [
  {
    icon: <Webhook />,
    title: "A partner calls Bitween",
    body: "On an API gateway, attach the partner and create its subscription there.",
    to: "/api-gateways",
    permission: "api-gateways.view",
  },
  {
    icon: <CalendarClock />,
    title: "On a schedule",
    body: "A scheduled job fetches documents from an HTTP API, storage, a mailbox or a database.",
    to: "/scheduled-jobs/new",
    permission: "subscriptions.create",
  },
  {
    icon: <Workflow />,
    title: "A message arrives on the bus",
    body: "On a bus gateway, add a route to a new subscription.",
    to: "/bus-gateways",
    permission: "bus-gateways.view",
  },
  {
    icon: <CornerDownLeft />,
    title: "Another subscription's response",
    body: "A response subscription takes what a delivery answered and carries it on.",
    to: "/response-subscriptions/new",
    permission: "subscriptions.create",
  },
  {
    icon: <Layers />,
    title: "Rolling exchanges up",
    body: "An aggregation collects finished exchanges on a schedule and delivers them together.",
    to: "/aggregations/new",
    permission: "subscriptions.create",
  },
];

export function NewSubscriptionDialog({ onClose }: { onClose: () => void }) {
  const navigate = useNavigate();
  const can = {
    "api-gateways.view": useSessionCan("api-gateways.view"),
    "bus-gateways.view": useSessionCan("bus-gateways.view"),
    "subscriptions.create": useSessionCan("subscriptions.create"),
  } as Record<string, boolean>;

  return (
    <Dialog title="What starts it?" onClose={onClose}>
      <p className="mb-3 text-[13px] text-ink-500">
        A subscription is created where whatever starts it lives, so it is wired in from the start.
      </p>
      <ul className="space-y-2">
        {WAYS.filter((w) => can[w.permission]).map((w) => (
          <li key={w.title}>
            <button
              type="button"
              onClick={() => navigate(w.to)}
              className="flex w-full items-center gap-3 rounded-lg border border-ink-200 px-3 py-2.5 text-left hover:border-ink-300 hover:bg-ink-50"
            >
              <span className="text-ink-500 [&>svg]:size-5">{w.icon}</span>
              <span className="min-w-0 flex-1">
                <span className="block text-[13.5px] font-medium text-ink-900">{w.title}</span>
                <span className="block text-[12.5px] text-ink-500">{w.body}</span>
              </span>
              <ChevronRight className="size-4 shrink-0 text-ink-400" aria-hidden />
            </button>
          </li>
        ))}
      </ul>
    </Dialog>
  );
}
