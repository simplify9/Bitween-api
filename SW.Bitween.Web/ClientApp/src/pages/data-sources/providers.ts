import { useQuery } from "@tanstack/react-query";

import { api } from "../../api";
import { keys } from "../../api/queryKeys";
import type { DataSourceProvider, DataSourceProviderSetting } from "../../api/types";

/**
 * The bus providers this deployment can offer.
 *
 * This used to be a hand-written table right here: the fields each provider starts with, their
 * defaults, their allowed values, which of them are credentials. That is a copy of a contract the
 * front end does not own, and it drifted — a hint told operators to set "Tls" while the adapter
 * only ever read "UseSsl", so a connection that looked encrypted in this form was in the clear on
 * the wire.
 *
 * The adapter declares its own settings now and the server reads them straight out of the package,
 * so a provider added or a setting renamed shows up here without anyone editing the UI.
 */
export const useDataSourceProviders = () =>
  useQuery({
    queryKey: keys.dataSources.providers,
    queryFn: () => api.listDataSourceProviders(),
    // Only changes when an adapter is republished, which is not something a session sees.
    staleTime: 10 * 60 * 1000,
  });

export const providerOf = (
  providers: DataSourceProvider[] | undefined,
  adapterId: string,
): DataSourceProvider | undefined => providers?.find((p) => p.adapterId === adapterId);

export const settingOf = (
  provider: DataSourceProvider | undefined,
  name: string,
): DataSourceProviderSetting | undefined =>
  provider?.settings.find((s) => s.name.toLowerCase() === name.toLowerCase());

/**
 * The properties a new data source starts with: everything the adapter said is required, plus
 * everything it gave a default. The rest stays off the form until someone adds it, because a form
 * of twenty mostly-empty boxes hides the four that matter.
 */
export const initialProperties = (provider: DataSourceProvider): Record<string, string> =>
  Object.fromEntries(
    provider.settings
      .filter((s) => s.required || s.default !== null)
      .map((s) => [s.name, s.default ?? ""]),
  );

/**
 * The order to show a data source's settings in: the adapter's, not the dictionary's.
 *
 * What comes back from the server is a map, and a map has whatever order the database and the
 * serializer between them produced — which is how a form ended up asking for a password before the
 * username it belongs to. The adapter declares its settings in a deliberate order (connection
 * first, then credentials, then tuning), and that is the order to render.
 *
 * Anything not declared was added by hand, so it keeps its own order and follows the declared set
 * rather than being interleaved into it.
 */
export const orderedSettingNames = (
  provider: DataSourceProvider | undefined,
  names: string[],
): string[] => {
  const rank = new Map(provider?.settings.map((s, i) => [s.name.toLowerCase(), i]) ?? []);
  const declared = names
    .filter((n) => rank.has(n.toLowerCase()))
    .sort((a, b) => rank.get(a.toLowerCase())! - rank.get(b.toLowerCase())!);
  const rest = names.filter((n) => !rank.has(n.toLowerCase()));
  return [...declared, ...rest];
};

export const declaredSecrets = (provider: DataSourceProvider): string[] =>
  provider.settings.filter((s) => s.secret).map((s) => s.name);

/**
 * Whether a setting holds a credential. The adapter says so for the settings it declares, and the
 * backend masks on the same rule; this covers a property someone added by hand, before it has ever
 * been saved.
 */
const CREDENTIAL =
  /password|secret|token|credential|apikey|accesskey|privatekey|connectionstring|sas|passphrase|certificate/i;

export const isSecretName = (
  name: string,
  declared: string[] = [],
  setting?: DataSourceProviderSetting,
): boolean =>
  setting?.secret === true ||
  declared.some((d) => d.toLowerCase() === name.toLowerCase()) ||
  CREDENTIAL.test(name);

/**
 * Which adapters bind to a data source at all, as a hook so every page asking the
 * question shares one answer — the subscription's own page and all three create pages.
 *
 * A relational one always does; a BROKER one does in a delivery, because that is how a
 * subscription answers on the customer's own queue — ingress from a broker comes through
 * a bus gateway rather than through a subscription slot.
 */
export function useBindsToDataSource() {
  const providers = useDataSourceProviders();
  return (adapterId: string | null, slot: "receiver" | "mapper" | "handler") => {
    const kind = adapterId == null ? null : providerOf(providers.data, adapterId)?.kind;
    if (kind === "Relational") return true;
    return kind === "Broker" && slot !== "receiver";
  };
}

/** Words an adapter spells in PascalCase that read wrong once lower-cased. */
const ACRONYMS: Record<string, string> = {
  ssl: "SSL", tls: "TLS", url: "URL", uri: "URI", id: "ID", sql: "SQL", ttl: "TTL", tcp: "TCP",
  ms: "ms",
};

/**
 * A setting's name as a label: "UserName" reads "User name", "UseSsl" reads "Use SSL". Only the
 * label changes — the adapter still receives the setting under its own name.
 */
export const settingLabel = (name: string): string => {
  const words = name
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2")
    .split(/[\s_.-]+/)
    .filter(Boolean);
  return words
    .map((word, i) => {
      const acronym = ACRONYMS[word.toLowerCase()];
      if (acronym) return acronym;
      if (word.length > 1 && word === word.toUpperCase()) return word;
      return i === 0 ? word[0].toUpperCase() + word.slice(1).toLowerCase() : word.toLowerCase();
    })
    .join(" ");
};
