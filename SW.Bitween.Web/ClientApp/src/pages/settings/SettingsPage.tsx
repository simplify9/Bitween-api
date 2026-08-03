import { useEffect, useState } from "react";
import { Link, useSearchParams } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { RotateCcw } from "lucide-react";
import { api, resetAppConfig, type SettingRow } from "../../api";
import { useSessionCan } from "../../auth/guards";
import { PageHeader } from "../../components/layout/PageHeader";
import { Badge, Button, LoadingBlock } from "../../components/ui/basics";
import { Checkbox, TextInput } from "../../components/ui/forms";
import { UnsavedBar } from "../../components/ui/Panel";
import { settingsDraft, useSettingsDraft } from "../../lib/settingsDraft";

/** Sections, in the order the backend catalog lists them. */
const sectionsOf = (rows: SettingRow[]): string[] => [...new Set(rows.map((r) => r.section))];

/**
 * The one section with a reset-the-lot button, and deliberately the only one.
 * Everything here is cosmetic and re-enterable from what you can see on screen;
 * other sections hold values the database is the sole home for — the Rebex
 * license key, the MSAL client ids — where one click is far too little standing
 * between an administrator and config nobody can reconstruct. Per-row "Reset to
 * default" still covers those, one considered decision at a time.
 */
const BRAND_SECTION = "Brand & theme";

const formatDefault = (row: SettingRow): string => {
  if (row.kind === "boolean") return row.defaultValue === "true" ? "On" : "Off";
  return row.defaultValue.trim() ? row.defaultValue : "(empty)";
};

const formatValue = (row: SettingRow, value: string): string => {
  if (!value.trim()) return "Not set";
  return row.kind === "boolean" ? (value === "true" ? "On" : "Off") : value;
};

/**
 * A setting this instance was started with. Read once at startup, so there's nothing to change
 * here — the row exists so an administrator can see what the instance is actually running on
 * without shelling into it. A `presence` row's value never leaves the server.
 */
function EnvironmentSettingRow({ row }: { row: SettingRow }) {
  return (
    <div className="grid grid-cols-1 gap-x-5 gap-y-1.5 py-3 first:pt-0 last:pb-0 sm:grid-cols-[17rem_1fr_auto] sm:items-start">
      <div className="min-w-0">
        <div className="flex flex-wrap items-center gap-1.5">
          <span className="text-sm font-medium text-ink-800">{row.label}</span>
          <Badge>Environment</Badge>
        </div>
        <p className="mt-0.5 text-[13px] text-ink-500">{row.description}</p>
      </div>

      <div className="min-w-0 sm:pt-0.5">
        {row.access === "presence" ? (
          <Badge tone={row.hasValue ? "ok" : "neutral"}>{row.hasValue ? "Set" : "Not set"}</Badge>
        ) : (
          <span
            className={`font-mono text-sm break-all ${row.value?.trim() ? "text-ink-700" : "text-ink-400"}`}
          >
            {formatValue(row, row.value ?? "")}
          </span>
        )}
      </div>

      <div />
    </div>
  );
}

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

  const [searchParams] = useSearchParams();
  const sections = sectionsOf(rows ?? []);
  const fromUrl = searchParams.get("section");
  const section = fromUrl && sections.includes(fromUrl) ? fromUrl : sections[0];
  // Real links, so a section is a URL an administrator can paste into a ticket.
  // Replacing rather than pushing: switching section isn't a step you want Back to undo.
  const searchFor = (s: string) => {
    const next = new URLSearchParams(searchParams);
    next.set("section", s);
    return `?${next.toString()}`;
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

  // Rows a section-wide reset would actually change: a stored value that differs from the
  // product default, or an edit staged this session. Rows already staged for reset are
  // excluded, so the button stops offering itself once there is nothing left to do.
  const resettable =
    section === BRAND_SECTION && canEdit
      ? sectionRows.filter(
          (r) =>
            r.access === "editable" &&
            r.editable &&
            (r.key in draft ? draft[r.key] !== null : r.overridden),
        )
      : [];

  return (
    <div className={dirtyCount > 0 ? "pb-20" : ""}>
      <PageHeader
        title="Settings"
        description="Instance-wide configuration. Changes preview live across the app — walk around and look, then save (or discard) here. Rows marked Environment are fixed by how this instance was started."
      />

      {/* A left column, not a row of pill tabs: eight sections wrap to a second row on a
          narrow window, and a wrapped tab row is precisely where you lose track of which
          one you're in. House pattern for anything that wanted tabs. On a phone it stacks
          above the rows — settings is a desk job, and a list that never wraps beats one
          that sometimes does. */}
      <div className="grid grid-cols-1 gap-x-6 gap-y-3 lg:grid-cols-[13.5rem_minmax(0,48rem)] lg:items-start">
        <nav aria-label="Settings sections" className="lg:sticky lg:top-4">
          <ul className="space-y-0.5">
            {sections.map((s) => {
              const active = s === section;
              const hasUnsaved = rows.some((r) => r.section === s && r.key in draft);
              // Standing count of rows this instance has moved off the product default —
              // the answer to "what has been done to this instance", visible without
              // opening all eight sections.
              const customized = rows.filter((r) => r.section === s && r.overridden).length;
              return (
                <li key={s}>
                  <Link
                    to={searchFor(s)}
                    replace
                    aria-current={active ? "true" : undefined}
                    className={`relative flex items-center justify-between gap-2 rounded-lg py-1.5 pr-2.5 pl-3 text-sm transition-colors ${
                      active
                        ? "bg-ink-100 font-medium text-ink-900"
                        : "text-ink-600 hover:bg-ink-50 hover:text-ink-900"
                    }`}
                  >
                    {active && (
                      <span
                        className="absolute inset-y-1 left-0 w-1 rounded-r-full bg-crimson-600"
                        aria-hidden
                      />
                    )}
                    <span className="truncate">{s}</span>
                    {hasUnsaved ? (
                      <span
                        className="size-1.5 shrink-0 rounded-full bg-crimson-600"
                        title="Unsaved changes"
                        aria-hidden
                      />
                    ) : (
                      customized > 0 && (
                        <span
                          className="shrink-0 text-[11px] tabular-nums text-ink-400"
                          title={`${customized} changed from the default`}
                        >
                          {customized}
                        </span>
                      )
                    )}
                  </Link>
                </li>
              );
            })}
          </ul>
        </nav>

        <div className="rounded-xl border border-ink-200 bg-white p-5">
          {/* The active pill used to be what named the panel; with the nav off to the
              side the panel has to name itself. */}
          <div className="mb-4 border-b border-ink-100 pb-3">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <h2 className="text-[15px] font-semibold text-ink-900">{section}</h2>
              {/* Stages a reset per row rather than writing: the draft previews the stock
                  branding across the whole app, and Discard puts it all back untouched. */}
              {resettable.length > 0 && (
                <Button size="sm" onClick={() => resettable.forEach((r) => settingsDraft.stage(r, null))}>
                  <RotateCcw className="size-3.5" aria-hidden />
                  Reset all to default
                </Button>
              )}
            </div>
            {resettable.length > 0 && (
              <p className="mt-1.5 text-[13px] text-ink-500">
                {resettable.length} of these {resettable.length === 1 ? "is" : "are"} customized.
                Resetting stages the change like any other — nothing is written until you save.
              </p>
            )}
          </div>
          <div className="divide-y divide-ink-100">
            {sectionRows.map((row) =>
              row.access === "editable" ? (
                <SettingRowEditor key={row.key} row={row} staged={draft[row.key]} canEdit={canEdit} />
              ) : (
                <EnvironmentSettingRow key={row.key} row={row} />
              ),
            )}
          </div>
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
