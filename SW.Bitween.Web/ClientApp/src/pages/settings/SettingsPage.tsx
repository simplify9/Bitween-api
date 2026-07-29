import { useEffect, useState } from "react";
import { useSearchParams } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, resetAppConfig, type SettingRow } from "../../api";
import { useSessionCan } from "../../auth/guards";
import { PageHeader } from "../../components/layout/PageHeader";
import { Badge, LoadingBlock } from "../../components/ui/basics";
import { Checkbox, TextInput } from "../../components/ui/forms";
import { UnsavedBar } from "../../components/ui/Panel";
import { settingsDraft, useSettingsDraft } from "../../lib/settingsDraft";

/** Sections, in the order the backend catalog lists them. */
const sectionsOf = (rows: SettingRow[]): string[] => [...new Set(rows.map((r) => r.section))];

const formatDefault = (row: SettingRow): string => {
  if (row.kind === "boolean") return row.defaultValue === "true" ? "On" : "Off";
  return row.defaultValue.trim() ? row.defaultValue : "(empty)";
};

function SettingRowEditor({
  row,
  staged,
  canEdit,
}: {
  row: SettingRow;
  /** undefined = untouched; null = reset-to-default pending; string = new value pending. */
  staged: string | null | undefined;
  canEdit: boolean;
}) {
  const effective = staged !== undefined ? (staged ?? row.defaultValue) : (row.value ?? row.defaultValue);
  const [draft, setDraft] = useState(effective);
  const [replacing, setReplacing] = useState(false);
  const [focused, setFocused] = useState(false);

  useEffect(() => {
    if (!focused) {
      setDraft(effective);
      if (staged === undefined) setReplacing(false);
    }
  }, [effective, focused, staged]);

  const stage = (value: string | null) => settingsDraft.stage(row, value);

  const commitIfChanged = (value: string) => {
    setFocused(false);
    if (value !== effective) stage(value);
  };

  // Shows as overridden: a pending value, or (untouched and) a stored value that isn't the default.
  const showsOverridden = staged !== undefined ? staged !== null : row.overridden;
  // A secret's value never reaches the browser, so "there is one" is all we can show.
  const masked = row.secret && row.hasValue && staged === undefined && !replacing;
  // `editable: false` means a secret with no encryption key configured on this instance.
  const disabled = !canEdit || !row.editable;

  return (
    <div className="grid grid-cols-1 gap-x-5 gap-y-1.5 py-3 first:pt-0 last:pb-0 sm:grid-cols-[17rem_1fr_auto] sm:items-start">
      <div className="min-w-0">
        <div className="flex flex-wrap items-center gap-1.5">
          <span className="text-sm font-medium text-ink-800">{row.label}</span>
          {row.secret && <Badge>Secret</Badge>}
          {staged !== undefined && <Badge tone="crimson">Unsaved</Badge>}
        </div>
        <p className="mt-0.5 text-[13px] text-ink-500">{row.description}</p>
      </div>

      <div className="min-w-0">
        {row.kind === "boolean" ? (
          <Checkbox
            label={effective === "true" ? "On" : "Off"}
            checked={effective === "true"}
            disabled={disabled}
            onChange={(e) => stage(e.target.checked ? "true" : "false")}
          />
        ) : row.kind === "color" ? (
          <div className="flex items-center gap-2">
            <input
              type="color"
              aria-label={`${row.label} picker`}
              value={/^#[0-9a-fA-F]{6}$/.test(draft) ? draft : effective}
              disabled={disabled}
              onChange={(e) => {
                setDraft(e.target.value);
                stage(e.target.value);
              }}
              className="h-9.5 w-12 shrink-0 cursor-pointer rounded-lg border border-ink-200 bg-white p-1 disabled:cursor-not-allowed"
            />
            <TextInput
              type="text"
              aria-label={row.label}
              value={draft}
              disabled={disabled}
              onFocus={() => setFocused(true)}
              onChange={(e) => setDraft(e.target.value)}
              onBlur={(e) => {
                if (/^#[0-9a-fA-F]{6}$/.test(e.target.value)) {
                  commitIfChanged(e.target.value);
                } else {
                  setFocused(false);
                  setDraft(effective);
                }
              }}
              className="max-w-40 font-mono uppercase"
            />
          </div>
        ) : masked ? (
          <div className="flex h-9.5 items-center justify-between rounded-lg border border-ink-200 bg-ink-50 px-3">
            <span className="font-mono text-sm tracking-widest text-ink-400">••••••••</span>
            {!disabled && (
              <button
                type="button"
                onClick={() => {
                  setReplacing(true);
                  setDraft("");
                }}
                className="text-[13px] font-medium text-crimson-700 hover:underline"
              >
                Replace
              </button>
            )}
          </div>
        ) : (
          <TextInput
            aria-label={row.label}
            value={draft}
            disabled={disabled}
            placeholder={row.defaultValue}
            type={row.kind === "number" ? "number" : "text"}
            onFocus={() => setFocused(true)}
            onChange={(e) => setDraft(e.target.value)}
            onBlur={(e) => {
              if (row.kind === "number" && e.target.value.trim() !== "" && Number.isNaN(Number(e.target.value))) {
                setFocused(false);
                setDraft(effective);
                return;
              }
              commitIfChanged(e.target.value.trim());
            }}
          />
        )}

        {!row.editable ? (
          <p className="mt-1 text-xs text-warn-700">
            Stored secrets need <span className="font-mono">Bitween:SettingsEncryptionKey</span> configured on this
            instance. Until then this value comes from configuration and can't be changed here.
          </p>
        ) : (
          !row.secret &&
          !showsOverridden && (
            <p className="mt-1 text-xs text-ink-400">
              Default: <span className="font-mono">{formatDefault(row)}</span>
            </p>
          )
        )}
      </div>

      <div className="sm:pt-1.5">
        {showsOverridden && canEdit && row.editable && (
          <button
            type="button"
            onClick={() => stage(null)}
            className="text-[13px] font-medium whitespace-nowrap text-crimson-700 hover:underline"
          >
            Reset to default
          </button>
        )}
      </div>
    </div>
  );
}

/**
 * Edits stage into a draft (never saved on the spot). The draft previews
 * live across the whole app — leave this page and look around; the shell's
 * banner brings you back. Save writes everything; Discard reverts it all.
 */
export function SettingsPage() {
  const canEdit = useSessionCan("settings.edit");
  const queryClient = useQueryClient();
  const { data: rows, isLoading } = useQuery({ queryKey: ["settings"], queryFn: () => api.listSettings() });
  const draft = useSettingsDraft();

  const [searchParams, setSearchParams] = useSearchParams();
  const sections = sectionsOf(rows ?? []);
  const fromUrl = searchParams.get("section");
  const section = fromUrl && sections.includes(fromUrl) ? fromUrl : sections[0];
  const setSection = (s: string) => {
    const next = new URLSearchParams(searchParams);
    next.set("section", s);
    setSearchParams(next, { replace: true });
  };

  const saveAll = useMutation({
    mutationFn: async () => {
      for (const [key, value] of Object.entries(settingsDraft.get())) {
        await api.updateSetting(key, value);
      }
    },
    onSuccess: () => {
      settingsDraft.discardAll();
      void queryClient.invalidateQueries({ queryKey: ["settings"] });
      // Branding everywhere reads the memoised config payload, so it has to be re-fetched
      // for a saved brand change to stick once the draft preview is dropped.
      resetAppConfig();
      void queryClient.invalidateQueries({ queryKey: ["appConfig"] });
    },
  });

  if (isLoading) return <LoadingBlock label="Loading settings…" />;
  if (!rows) return null;

  const dirtyCount = Object.keys(draft).length;
  const sectionRows = rows.filter((r) => r.section === section);

  return (
    <div className={dirtyCount > 0 ? "pb-20" : ""}>
      <PageHeader
        title="Settings"
        description="Instance-wide configuration. Changes preview live across the app — walk around and look, then save (or discard) here."
      />

      <div className="mb-4 flex flex-wrap gap-2">
        {sections.map((s) => {
          const active = s === section;
          const hasUnsaved = rows.some((r) => r.section === s && r.key in draft);
          return (
            <button
              key={s}
              type="button"
              onClick={() => setSection(s)}
              aria-pressed={active}
              className={`inline-flex items-center gap-1.5 rounded-full px-3 py-1.5 text-[13px] font-medium transition-colors ${
                active
                  ? "bg-ink-900 text-white"
                  : "border border-ink-200 bg-white text-ink-600 hover:border-ink-300 hover:bg-ink-50"
              }`}
            >
              {s}
              {hasUnsaved && <span className="size-1.5 rounded-full bg-crimson-600" title="Unsaved changes" aria-hidden />}
            </button>
          );
        })}
      </div>

      <div className="max-w-3xl rounded-xl border border-ink-200 bg-white p-5">
        <div className="divide-y divide-ink-100">
          {sectionRows.map((row) => (
            <SettingRowEditor key={row.key} row={row} staged={draft[row.key]} canEdit={canEdit} />
          ))}
        </div>
      </div>

      {dirtyCount > 0 && (
        <UnsavedBar
          busy={saveAll.isPending}
          error={saveAll.error?.message}
          onSave={() => saveAll.mutate()}
          onDiscard={() => settingsDraft.discardAll()}
        />
      )}
    </div>
  );
}
