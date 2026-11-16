import { useEffect, useState } from "react";
import { PlugZap } from "lucide-react";
import type { AiCandidate, AiProvider, SettingRow } from "../../api";
import { Badge, Button, ProblemText } from "../../components/ui/basics";
import { Select, TextInput } from "../../components/ui/forms";
import { SearchSelect } from "../../components/ui/SearchSelect";
import {
  fillTemplate,
  hasOwnAddress,
  ownsAddress,
  partOf,
  type AiCheckOutcome,
  type useAiModels,
} from "../../lib/aiSettings";
import { settingsDraft } from "../../lib/settingsDraft";

/**
 * The Model setting as a searchable list of what the provider offers, where any other name can
 * still be typed - a model newer than the list, or an Azure deployment, which no list names.
 */
export function AiModelPicker({
  row,
  value,
  disabled,
  models,
  onChange,
}: {
  row: SettingRow;
  value: string;
  disabled: boolean;
  models: ReturnType<typeof useAiModels>;
  onChange: (value: string) => void;
}) {
  const provider = models.provider;
  const name = provider?.name ?? "the provider";
  const isAnthropic = provider?.name === "Anthropic";
  const listed = models.data ?? [];
  // The saved name stays a choice even when the provider no longer lists it, so the picker shows it.
  const options = [...new Set([...(value ? [value] : []), ...listed])].map((m) => ({ value: m, label: m }));

  return (
    <div className="space-y-1">
      <SearchSelect
        aria-label={row.label}
        value={value}
        disabled={disabled}
        onChange={onChange}
        options={options}
        placeholder={isAnthropic ? "Default model" : "Pick or type a model"}
        clearLabel={isAnthropic ? "Default model" : undefined}
        freeText={(typed) => ({ value: typed, label: `Use “${typed}”` })}
      />
      <p className="text-xs text-ink-500">
        {provider && !provider.lists
          ? provider.modelHint
          : !models.hasAddress
            ? "Set the address to list the models."
            : !models.ready
              ? "Set the API key to list the models."
              : models.isFetching
                ? `Loading ${name}'s models…`
                : models.error
                  ? `Couldn't list models: ${models.error.message}`
                  : `${listed.length} model${listed.length === 1 ? "" : "s"} from ${name}.`}
      </p>
    </div>
  );
}

/**
 * Where AI requests go, asked the way the provider is set up: nothing for one with its own
 * address, a region for one with several, the part only the customer knows (an Azure resource,
 * an AWS region) for one built from a template - and a whole address only for a proxy or a
 * server Bitween doesn't know. Whatever is asked, it's stored as the address it makes.
 */
export function AiAddressRow({
  row,
  staged,
  provider,
  canEdit,
}: {
  row: SettingRow;
  staged: string | null | undefined;
  provider: AiProvider;
  canEdit: boolean;
}) {
  const value = (staged !== undefined ? staged : row.value) ?? "";
  const stage = (next: string) => settingsDraft.stage(row, next);
  const disabled = !canEdit || !row.editable;

  // A whole address: one that isn't the provider's own, or one asked for to go through a proxy.
  const [custom, setCustom] = useState(!hasOwnAddress(provider) || !ownsAddress(provider, value));
  useEffect(() => {
    setCustom(!hasOwnAddress(provider) || !ownsAddress(provider, value));
    // Only when the provider changes: typing an address mustn't flip the mode under the cursor.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [provider.name]);
  // And when the saved address comes back (Discard), so the mode matches it again.
  const restored = staged === undefined;
  useEffect(() => {
    if (restored) setCustom(!hasOwnAddress(provider) || !ownsAddress(provider, value));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [restored]);

  const templatePart = provider.template ? (partOf(provider.template, value) ?? "") : "";
  const [part, setPart] = useState(templatePart);
  const [address, setAddress] = useState(value);
  useEffect(() => setPart(templatePart), [templatePart]);
  useEffect(() => setAddress(value), [value]);

  const partBad = part.trim() !== "" && !/^[A-Za-z0-9-]+$/.test(part.trim());
  const label = custom ? "API address" : (provider.part ?? (provider.places?.length ? "Region" : "API address"));
  const calls = custom
    ? null
    : provider.template
      ? part.trim() && !partBad
        ? fillTemplate(provider.template, part.trim())
        : null
      : value || provider.address;

  return (
    <div className="grid grid-cols-1 gap-x-5 gap-y-1.5 py-3 first:pt-0 last:pb-0 sm:grid-cols-[17rem_1fr_auto] sm:items-start">
      <div className="min-w-0">
        <div className="flex flex-wrap items-center gap-1.5">
          <span className="text-sm font-medium text-ink-800">{label}</span>
          {staged !== undefined && <Badge tone="crimson">Unsaved</Badge>}
        </div>
        {custom && <p className="mt-0.5 text-[13px] text-ink-500">{row.description}</p>}
      </div>

      <div className="min-w-0 space-y-1">
        {custom ? (
          <TextInput
            aria-label="API address"
            value={address}
            disabled={disabled}
            placeholder={provider.example ?? provider.address ?? ""}
            onChange={(e) => setAddress(e.target.value)}
            onBlur={(e) => {
              if (e.target.value.trim() !== value) stage(e.target.value.trim());
            }}
          />
        ) : provider.template ? (
          <TextInput
            aria-label={provider.part ?? "Address part"}
            value={part}
            disabled={disabled}
            placeholder={provider.example ?? ""}
            aria-invalid={partBad || undefined}
            onChange={(e) => setPart(e.target.value)}
            onBlur={(e) => {
              const typed = e.target.value.trim();
              if (typed !== "" && !/^[A-Za-z0-9-]+$/.test(typed)) return;
              const next = typed === "" ? "" : fillTemplate(provider.template!, typed);
              if (next !== value) stage(next);
            }}
            className="max-w-60"
          />
        ) : provider.places?.length ? (
          <Select
            aria-label="Region"
            value={value || provider.address || provider.places[0].address}
            disabled={disabled}
            // The provider's own address is stored as empty, as for one with no regions.
            onChange={(e) => stage(e.target.value === provider.address ? "" : e.target.value)}
            options={provider.places.map((p) => ({ value: p.address, label: p.label }))}
            className="max-w-60"
          />
        ) : null}

        {partBad && <p className="text-xs text-danger-700">Letters, digits and dashes only.</p>}
        {calls && (
          <p className="text-xs break-all text-ink-500">
            {provider.template || provider.places?.length ? "Calls " : ""}
            <span className="font-mono">{calls}</span>
          </p>
        )}
        {!disabled && hasOwnAddress(provider) && (
          <button
            type="button"
            className="text-[13px] font-medium link"
            title={custom ? `Go back to ${provider.name}'s own address.` : "For a proxy or gateway in front of the provider."}
            onClick={() => {
              if (custom) {
                setCustom(false);
                if (!ownsAddress(provider, value)) stage("");
              } else {
                setCustom(true);
              }
            }}
          >
            {custom ? `Use ${provider.name}'s address` : "Use a different address"}
          </button>
        )}
      </div>

      <div />
    </div>
  );
}

/**
 * Tests the AI settings on screen, saved or not, with the same kind of call the AI features make -
 * so a wrong key, model or address shows here rather than in the middle of a mapping.
 */
export function AiTestBar({
  candidate,
  outcomeKey,
  outcome,
  busy,
  canEdit,
  onTest,
}: {
  candidate: AiCandidate;
  /** Names the settings on screen, as an outcome names the ones it was for. */
  outcomeKey: string;
  outcome: AiCheckOutcome | null;
  busy: boolean;
  canEdit: boolean;
  onTest: () => void;
}) {
  // An outcome for other settings than the ones showing says nothing about these.
  const current = outcome?.for === outcomeKey ? outcome : null;
  return (
    <div className="mb-4 flex flex-wrap items-center gap-x-3 gap-y-2 rounded-lg border border-ink-200 bg-ink-50 px-3 py-2.5">
      <Button
        size="sm"
        busy={busy}
        disabled={!canEdit}
        onClick={onTest}
        title="Sends one small request with the settings shown, saved or not."
      >
        <PlugZap className="size-3.5" aria-hidden />
        Test connection
      </Button>
      <div className="min-w-0 flex-1 text-[13px]">
        {busy ? (
          <span className="text-ink-500">Calling {candidate.provider}…</span>
        ) : current?.result ? (
          <span className="flex flex-wrap items-center gap-1.5 text-ink-700">
            <Badge tone="ok" title="The model answered with JSON, as AI features need.">
              Works
            </Badge>
            <span className="font-mono">{current.result.model}</span> answered in{" "}
            {(current.result.milliseconds / 1000).toFixed(1)} s.
          </span>
        ) : current?.error ? (
          <ProblemText>{current.error}</ProblemText>
        ) : (
          <span className="text-ink-500">Saving runs this test too.</span>
        )}
      </div>
    </div>
  );
}
