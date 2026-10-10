import { useState } from "react";
import { Link } from "react-router";
import { useQuery } from "@tanstack/react-query";
import { CheckCircle2, Circle, X } from "lucide-react";
import { api } from "../../api";
import { keys } from "../../api/queryKeys";
import { useSession } from "../../auth/useSession";
import { useSubscriptionsCache } from "../../components/config/lookups";

const DISMISSED_KEY = "bitween_getting_started_dismissed";

const dismissed = () => {
  try {
    return localStorage.getItem(DISMISSED_KEY) === "1";
  } catch {
    return false;
  }
};

/**
 * The order a new Bitween is set up in, with each step ticked off from what exists. Nothing said
 * where to start: an information type comes before a partner can send one, a partner and its key
 * before a gateway can let it in, and a subscription before anything moves.
 */
export function GettingStarted({ exchangesSeen }: { exchangesSeen: boolean }) {
  const { can } = useSession();
  const [hidden, setHidden] = useState(dismissed);
  const show = !hidden && can("subscriptions.create");

  const types = useQuery({
    queryKey: keys.informationTypes.list,
    queryFn: () => api.listInformationTypes(),
    enabled: show && can("documents.view"),
  });
  const partners = useQuery({ queryKey: keys.partners.list, queryFn: () => api.listPartners(), enabled: show && can("partners.view") });
  const gateways = useQuery({
    queryKey: keys.apiGateways.list,
    queryFn: () => api.listApiGateways(),
    enabled: show && can("api-gateways.view"),
  });
  const subscriptions = useSubscriptionsCache();

  if (!show || types.isPending || partners.isPending || subscriptions.isPending) return null;

  const all = subscriptions.data ?? [];
  const steps = [
    {
      done: (types.data?.length ?? 0) > 0,
      title: "Describe a document",
      body: "An information type says what a document is, and how to read and filter it.",
      to: "/information-types",
      action: "Information types",
    },
    {
      // Any partner of one's own: the list's key count isn't always filled in, and a partner set up
      // to send by token has no key at all.
      done: (partners.data ?? []).some((p) => !p.isSystem),
      title: "Add a partner and give it an API key",
      body: "Who sends or receives documents. The key is how the partner proves who it is.",
      to: "/partners",
      action: "Partners",
    },
    {
      done:
        (gateways.data ?? []).some((g) => (g.partnerCount ?? 0) > 0 || g.attachments.length > 0) ||
        all.some((s) => s.type === "Receiving"),
      title: "Let documents in",
      body: "Partners send documents to an API gateway, or a scheduled job fetches them on a schedule.",
      to: "/api-gateways",
      action: "API gateways",
    },
    {
      done: all.length > 0,
      title: "Say what happens to them",
      body: "A subscription maps a document and delivers it. Creating one while attaching a partner to a gateway wires it in at once.",
      to: "/subscriptions",
      action: "Subscriptions",
    },
    {
      done: exchangesSeen,
      title: "Send a first document",
      body: "Call the gateway, or create an exchange by hand, and follow it on the Exchanges page.",
      to: "/exchanges/new",
      action: "New exchange",
    },
  ];
  // Offered while the setup is unfinished; a working instance that just had a quiet fortnight
  // isn't told to start over.
  if (steps.slice(0, 4).every((s) => s.done)) return null;
  const next = steps.findIndex((s) => !s.done);

  return (
    <section aria-labelledby="getting-started" className="mb-5 rounded-xl border border-ink-200 bg-white p-5">
      <div className="flex items-start justify-between gap-3">
        <div>
          <h2 id="getting-started" className="text-[15px] font-semibold text-ink-900">
            Getting started
          </h2>
          <p className="mt-0.5 text-[13px] text-ink-500">
            {steps.filter((s) => s.done).length} of {steps.length} done. Each step needs the one before it.
          </p>
        </div>
        <button
          type="button"
          aria-label="Dismiss getting started"
          onClick={() => {
            try {
              localStorage.setItem(DISMISSED_KEY, "1");
            } catch {
              /* hidden for this visit only */
            }
            setHidden(true);
          }}
          className="rounded-md p-1 text-ink-500 hover:bg-ink-100 hover:text-ink-800"
        >
          <X className="size-4" />
        </button>
      </div>
      <ol className="mt-3 space-y-2.5">
        {steps.map((s, i) => (
          <li key={s.title} className="flex items-start gap-3">
            {s.done ? (
              <CheckCircle2 className="mt-0.5 size-4.5 shrink-0 text-ok-600" aria-label="Done" />
            ) : (
              <Circle className="mt-0.5 size-4.5 shrink-0 text-ink-300" aria-label="To do" />
            )}
            <div className="min-w-0 flex-1">
              <p className={`text-[13.5px] font-medium ${s.done ? "text-ink-500 line-through" : "text-ink-900"}`}>
                {i + 1}. {s.title}
              </p>
              {!s.done && <p className="text-[12.5px] text-ink-600">{s.body}</p>}
            </div>
            {!s.done && (
              <Link
                to={s.to}
                className={`shrink-0 rounded-lg px-3 py-1.5 text-[12.5px] font-medium ${
                  i === next ? "bg-crimson-600 text-white hover:bg-crimson-700" : "border border-ink-200 text-ink-700 hover:bg-ink-50"
                }`}
              >
                {s.action}
              </Link>
            )}
          </li>
        ))}
      </ol>
    </section>
  );
}
