import { useQuery } from "@tanstack/react-query";
import { api, type AiCandidate, type AiCheckResult, type AiProvider, type SettingRow } from "../api";
import { keys } from "../api/queryKeys";
import { settingsDraft, type SettingsDraft } from "./settingsDraft";

/** The catalog section the AI settings sit in. */
export const AI_SECTION = "AI";

export const AI_KEYS = {
  provider: "Bitween.Ai.Provider",
  apiKey: "Bitween.Ai.ApiKey",
  model: "Bitween.Ai.Model",
  baseUrl: "Bitween.Ai.BaseUrl",
} as const;

/** The providers Settings, AI offers, and how each one's address is made. They change only with a release. */
export function useAiProviders(wanted: boolean) {
  return useQuery({
    queryKey: keys.settings.aiProviders,
    queryFn: () => api.listAiProviders(),
    enabled: wanted,
    staleTime: Infinity,
  });
}

export const providerNamed = (providers: AiProvider[] | undefined, name: string) =>
  providers?.find((p) => p.name.toLowerCase() === name.toLowerCase());

/** The template's address with this part filled in. */
export const fillTemplate = (template: string, part: string) => template.replace("{0}", part);

/** The part an address fills into the template, or null when it isn't built from it. */
export function partOf(template: string, address: string): string | null {
  const [before, after] = template.split("{0}");
  if (!address.toLowerCase().startsWith(before.toLowerCase()) || !address.toLowerCase().endsWith(after.toLowerCase()))
    return null;
  const part = address.slice(before.length, address.length - after.length);
  return /^[A-Za-z0-9-]+$/.test(part) ? part : null;
}

/** Whether the provider can say where it is without an address being typed in whole. */
export const hasOwnAddress = (provider: AiProvider) => !!(provider.address || provider.places?.length || provider.template);

/** Whether an address is one of the provider's own: its address, a place, or its template filled in. */
export function ownsAddress(provider: AiProvider, address: string): boolean {
  if (!address) return true;
  if (provider.address === address) return true;
  if (provider.places?.some((p) => p.address === address)) return true;
  return !!provider.template && partOf(provider.template, address) !== null;
}

const valueOf = (rows: SettingRow[], draft: SettingsDraft, key: string) => {
  const row = rows.find((r) => r.key === key);
  const value = key in draft ? draft[key] : row?.value;
  return value ?? row?.defaultValue ?? "";
};

/**
 * The AI settings as they show on screen: staged values over saved ones. The key goes along only
 * when one was typed - a saved key never reaches the browser, and the server fills it in.
 */
export function aiCandidate(rows: SettingRow[], draft: SettingsDraft): AiCandidate {
  const typedKey = draft[AI_KEYS.apiKey];
  return {
    provider: valueOf(rows, draft, AI_KEYS.provider),
    apiKey: typedKey ? typedKey : undefined,
    model: valueOf(rows, draft, AI_KEYS.model),
    baseUrl: valueOf(rows, draft, AI_KEYS.baseUrl),
  };
}

/** Whether these settings have a key to call with: one typed, one saved (and not being cleared), or none needed. */
function hasKey(rows: SettingRow[], draft: SettingsDraft, provider: AiProvider | undefined): boolean {
  if (provider?.keyless) return true;
  if (AI_KEYS.apiKey in draft) return !!draft[AI_KEYS.apiKey];
  return rows.find((r) => r.key === AI_KEYS.apiKey)?.hasValue ?? false;
}

/**
 * What saving should test first: the AI settings on screen when some are staged and they still
 * have a key. Clearing the key turns AI off on purpose, and a section nobody has given a key yet
 * has nothing to call - neither is a failure worth stopping a save for.
 */
export function aiCheckOnSave(rows: SettingRow[], draft: SettingsDraft, providers: AiProvider[] | undefined): AiCandidate | null {
  if (!Object.values(AI_KEYS).some((key) => key in draft)) return null;
  const candidate = aiCandidate(rows, draft);
  return hasKey(rows, draft, providerNamed(providers, candidate.provider)) ? candidate : null;
}

/**
 * Stages another provider, with the key, model and address emptied - the last provider's are no use
 * to it, and saving clears them anyway. Going back to the saved provider brings its own back.
 */
export function changeProvider(rows: SettingRow[], value: string) {
  const row = (key: string) => rows.find((r) => r.key === key);
  const providerRow = row(AI_KEYS.provider);
  if (!providerRow) return;
  settingsDraft.stage(providerRow, value);
  const backToSaved = value === (providerRow.value ?? providerRow.defaultValue);
  for (const key of [AI_KEYS.apiKey, AI_KEYS.model, AI_KEYS.baseUrl]) {
    const r = row(key);
    if (!r) continue;
    if (backToSaved) settingsDraft.unstage(key);
    // A secret's saved value is null, so an empty string is what stages "no key".
    else settingsDraft.stage(r, "");
  }
}

/** The provider's model list for what's on screen. Asked for only while the AI section is open. */
export function useAiModels(rows: SettingRow[], draft: SettingsDraft, wanted: boolean, providers: AiProvider[] | undefined) {
  const candidate = aiCandidate(rows, draft);
  const provider = providerNamed(providers, candidate.provider);
  // The model isn't part of it: picking one shouldn't fetch the list again.
  const listedFor = { ...candidate, model: "" };
  const hasAddress = !!candidate.baseUrl || !!provider?.address;
  const ready = !!provider && hasKey(rows, draft, provider) && hasAddress;
  const query = useQuery({
    queryKey: keys.settings.aiModels(JSON.stringify(listedFor)),
    queryFn: () => api.listAiModels(listedFor),
    enabled: wanted && ready && provider.lists,
    staleTime: 5 * 60_000,
    retry: false,
  });
  return { ...query, ready, hasAddress, provider };
}

/** One test call's outcome, for the settings it was made with. */
export type AiCheckOutcome = { for: string; result?: AiCheckResult; error?: string };
