import { useState } from "react";
import { Link } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { BadgeCheck, Package, ShieldCheck, Store } from "lucide-react";
import { api, type MarketplaceCard, type MarketplaceInstalled, type RegistryTier } from "../../api";
import { keys } from "../../api/queryKeys";
import { Badge, Button, EmptyState, FormError, LoadError, LoadingBlock } from "../../components/ui/basics";
import { Checkbox, Select } from "../../components/ui/forms";
import { Dialog } from "../../components/ui/overlays";
import { SearchBox } from "../../components/ui/SearchBox";
import { Can } from "../../auth/guards";
import { formatDate } from "../../lib/dates";

const TIER_TONE: Record<RegistryTier, "crimson" | "ok" | "neutral"> = {
  Official: "crimson",
  Verified: "ok",
  Community: "neutral",
};

const TIER_HINT: Record<RegistryTier, string> = {
  Official: "Published by Simplify9.",
  Verified: "From a partner Simplify9 has vetted.",
  Community: "From a publisher Simplify9 hasn't vetted. Installing these is off unless Settings allows it.",
};

/**
 * Connectors from the Bitween Registry set under Settings. Installing one checks the registry's
 * and the publisher's signatures, then publishes it here like an uploaded package: it runs from
 * this Bitween's own storage and never needs the registry again.
 */
export function MarketplaceTab() {
  const [q, setQ] = useState("");
  const [tier, setTier] = useState("");
  const [open, setOpen] = useState<string | null>(null);
  const search = useQuery({ queryKey: keys.marketplace(q, tier), queryFn: () => api.searchMarketplace(q, tier) });

  if (search.isLoading) return <LoadingBlock label="Reading the registry…" />;
  if (search.error) return <LoadError error={search.error} what="the marketplace" onRetry={() => search.refetch()} />;

  if (!search.data?.configured)
    return (
      <EmptyState
        icon={<Store />}
        title={search.data?.disabled ? "The marketplace is turned off" : "No registry is set"}
        action={
          <Can permission="settings.view">
            <Link to="/settings" className="text-sm font-medium text-crimson-700 hover:underline">
              Open Settings
            </Link>
          </Can>
        }
      >
        {search.data?.disabled
          ? "An administrator turned it off under Settings, Marketplace. Turn it back on there to browse and install connectors."
          : "Set the registry's address and signing keys under Settings, Marketplace to browse and install connectors."}
      </EmptyState>
    );

  const results = search.data.result ?? [];

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center gap-3">
        <SearchBox value={q} onChange={setQ} label="Search the marketplace" />
        <Select
          aria-label="Publisher"
          value={tier}
          onChange={(e) => setTier(e.target.value)}
          options={[
            { value: "", label: "Every publisher" },
            { value: "Official", label: "Official" },
            { value: "Verified", label: "Verified" },
            { value: "Community", label: "Community" },
          ]}
        />
        <span className="ml-auto text-xs text-ink-500">From {search.data.registry}</span>
      </div>

      {results.length === 0 ? (
        <EmptyState icon={<Store />} title={q || tier ? "Nothing matches" : "No connectors yet"}>
          {q || tier ? "Try other words, or every publisher." : "The registry hasn't published any connectors yet."}
        </EmptyState>
      ) : (
        <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
          {results.map(({ card, installed }) => (
            <ConnectorCard key={card.id} card={card} installed={installed} onOpen={() => setOpen(card.id)} />
          ))}
        </div>
      )}

      {open && <ConnectorDialog id={open} onClose={() => setOpen(null)} />}
    </div>
  );
}

function ConnectorCard({ card, installed, onOpen }: { card: MarketplaceCard; installed: MarketplaceInstalled; onOpen: () => void }) {
  const update = installed.fromRegistry && !installed.versions.includes(card.latest);
  return (
    <button
      type="button"
      onClick={onOpen}
      className="flex flex-col rounded-xl border border-ink-200 bg-white p-4 text-left transition-colors hover:border-ink-300"
    >
      <div className="flex items-start gap-3">
        <Icon card={card} />
        <div className="min-w-0">
          <p className="truncate text-sm font-semibold text-ink-900">{card.displayName}</p>
          <p className="truncate text-xs text-ink-500">{card.publisherName}</p>
        </div>
      </div>
      <p className="mt-3 line-clamp-2 flex-1 text-sm text-ink-600">{card.summary}</p>
      <div className="mt-4 flex flex-wrap items-center gap-1.5">
        <Badge tone={TIER_TONE[card.tier]} title={TIER_HINT[card.tier]}>
          {card.tier}
        </Badge>
        {card.pricing === "Paid" && <Badge tone="warn">Paid</Badge>}
        {installed.fromRegistry && !update && <Badge tone="ok">Installed</Badge>}
        {update && <Badge tone="warn">Update available</Badge>}
        <span className="ml-auto text-xs text-ink-500">v{card.latest}</span>
      </div>
    </button>
  );
}

function ConnectorDialog({ id, onClose }: { id: string; onClose: () => void }) {
  const queryClient = useQueryClient();
  const detail = useQuery({ queryKey: keys.marketplacePackage(id), queryFn: () => api.getMarketplacePackage(id) });
  const [current, setCurrent] = useState(true);

  const install = useMutation({
    mutationFn: (version: string) => api.installFromMarketplace(id, version, current),
    onSuccess: () =>
      Promise.all([
        queryClient.invalidateQueries({ queryKey: ["marketplace"] }),
        queryClient.invalidateQueries({ queryKey: ["adapters"] }),
      ]),
  });

  const d = detail.data;
  const card = d?.package.card;
  const installed = d?.installed;
  const latest = card?.latest;
  const taken = installed?.installed && !installed.fromRegistry;
  const has = (version: string) => installed?.versions.includes(version) ?? false;

  return (
    <Dialog title={card?.displayName ?? id} onClose={onClose} wide>
      {detail.isLoading ? (
        <LoadingBlock />
      ) : detail.error ? (
        <LoadError error={detail.error} what="this connector" />
      ) : (
        d &&
        card && (
          <div className="space-y-5">
            <div className="flex items-start gap-3">
              <Icon card={card} />
              <div className="min-w-0 flex-1">
                <p className="text-sm text-ink-700">{card.summary}</p>
                <div className="mt-2 flex flex-wrap items-center gap-1.5 text-xs text-ink-500">
                  <Badge tone={TIER_TONE[card.tier]} title={TIER_HINT[card.tier]}>
                    {card.tier}
                  </Badge>
                  {card.pricing === "Paid" && <Badge tone="warn">Paid</Badge>}
                  <span>{card.publisherName}</span>
                  <span>·</span>
                  <code className="font-mono">{card.id}</code>
                  {card.runtime && (
                    <>
                      <span>·</span>
                      <span>{card.runtime}</span>
                    </>
                  )}
                </div>
              </div>
            </div>

            {d.package.description && <p className="whitespace-pre-line text-sm text-ink-700">{d.package.description}</p>}

            {d.latest?.properties && d.latest.properties.length > 0 && (
              <div>
                <h3 className="mb-2 text-xs font-semibold uppercase tracking-wide text-ink-500">Settings</h3>
                <ul className="divide-y divide-ink-100 rounded-lg border border-ink-200">
                  {d.latest.properties.map((p) => (
                    <li key={p.name} className="px-3 py-2 text-sm">
                      <code className="font-mono text-[13px] text-ink-800">{p.name}</code>
                      {(p.description || p.displayName) && (
                        <span className="ml-2 text-ink-500">{p.description ?? p.displayName}</span>
                      )}
                    </li>
                  ))}
                </ul>
              </div>
            )}

            {taken ? (
              <FormError>
                This Bitween already has an adapter named {card.id} that wasn't installed from the registry, so this
                connector can't be installed beside it.
              </FormError>
            ) : (
              <Can permission="adapter-source.operate">
                <div className="flex flex-wrap items-center gap-4 rounded-lg bg-ink-50 px-4 py-3">
                  <ShieldCheck className="size-5 text-ok-600" aria-hidden />
                  <p className="flex-1 text-sm text-ink-700">
                    Signed by {card.publisherName} and countersigned by the registry. Both are checked before
                    anything is installed.
                  </p>
                  <Checkbox label="Make it current" checked={current} onChange={(e) => setCurrent(e.target.checked)} />
                  {latest && (
                    <Button variant="primary" busy={install.isPending} disabled={has(latest)} onClick={() => install.mutate(latest)}>
                      {has(latest) ? `${latest} installed` : `Install ${latest}`}
                    </Button>
                  )}
                </div>
              </Can>
            )}
            {install.error && <FormError>{install.error.message}</FormError>}
            {install.data && (
              <p className="flex items-center gap-2 text-sm text-ok-600">
                <BadgeCheck className="size-4" aria-hidden />
                Installed {install.data.adapterId} {install.data.version}
                {install.data.current ? ", now current." : ". Make it current from the Installed tab."}
              </p>
            )}

            <div>
              <h3 className="mb-2 text-xs font-semibold uppercase tracking-wide text-ink-500">Versions</h3>
              <ul className="divide-y divide-ink-100 rounded-lg border border-ink-200">
                {d.package.versions.map((v) => (
                  <li key={v.version} className="flex flex-wrap items-center gap-3 px-3 py-2 text-sm">
                    <code className="font-mono text-ink-800">{v.version}</code>
                    {v.status !== "Published" && <Badge tone={v.status === "Revoked" ? "danger" : "neutral"}>{v.status}</Badge>}
                    {has(v.version) && <Badge tone="ok">Installed</Badge>}
                    {installed?.current === v.version && <Badge tone="ink">Current</Badge>}
                    <span className="text-ink-500">{v.minBitweenVersion ? `Bitween ${v.minBitweenVersion}+` : ""}</span>
                    <span className="ml-auto text-ink-500">{v.signedOn ? formatDate(v.signedOn) : ""}</span>
                    {v.status === "Published" && v.version !== latest && !has(v.version) && !taken && (
                      <Can permission="adapter-source.operate">
                        <Button size="sm" busy={install.isPending && install.variables === v.version} onClick={() => install.mutate(v.version)}>
                          Install
                        </Button>
                      </Can>
                    )}
                    {v.revokeReason && <p className="w-full text-xs text-danger-700">{v.revokeReason}</p>}
                  </li>
                ))}
              </ul>
            </div>
          </div>
        )
      )}
    </Dialog>
  );
}

function Icon({ card }: { card: MarketplaceCard }) {
  return card.iconDataUri ? (
    <img src={card.iconDataUri} alt="" className="size-10 rounded-lg" />
  ) : (
    <div className="flex size-10 shrink-0 items-center justify-center rounded-lg bg-ink-100 text-ink-500">
      <Package className="size-5" aria-hidden />
    </div>
  );
}
