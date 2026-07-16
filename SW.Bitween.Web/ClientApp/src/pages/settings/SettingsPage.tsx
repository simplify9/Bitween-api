import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { TriangleAlert } from "lucide-react";
import { api, type SettingRow, type SettingSection } from "../../api";
import { useSessionCan } from "../../auth/guards";
import { PageHeader } from "../../components/layout/PageHeader";
import { applyColorScale } from "../../lib/colorScale";
import { Badge, FormError, LoadingBlock } from "../../components/ui/basics";
import { Checkbox, TextInput } from "../../components/ui/forms";

const SECTIONS: SettingSection[] = [
  "Documents & storage",
  "API behavior",
  "Single sign-on (Microsoft)",
  "Messaging",
  "Adapters",
  "Reliability & jobs",
  "Security",
  "Brand & theme",
];

const formatDefault = (row: SettingRow): string => {
  if (row.kind === "boolean") return row.defaultValue === "true" ? "On" : "Off";
  if (row.kind === "string[]") return row.defaultValue.trim() ? row.defaultValue : "(none)";
  return row.defaultValue.trim() ? row.defaultValue : "(empty)";
};

function SettingRowEditor({
  row,
  canEdit,
  onSave,
}: {
  row: SettingRow;
  canEdit: boolean;
  onSave: (value: string | null) => void;
}) {
  const effective = row.value ?? row.defaultValue;
  const [draft, setDraft] = useState(effective);
  const [replacing, setReplacing] = useState(false);
  const [focused, setFocused] = useState(false);

  useEffect(() => {
    if (!focused) {
      setDraft(effective);
      setReplacing(false);
    }
  }, [effective, focused]);

  const commitIfChanged = (value: string) => {
    setFocused(false);
    if (value !== effective) onSave(value);
    setReplacing(false);
  };

  const masked = row.secret && row.overridden && !replacing;
  const disabled = !canEdit;

  return (
    <div className="grid grid-cols-1 gap-x-5 gap-y-1.5 py-3 first:pt-0 last:pb-0 sm:grid-cols-[17rem_1fr_auto] sm:items-start">
      <div className="min-w-0">
        <div className="flex flex-wrap items-center gap-1.5">
          <span className="text-sm font-medium text-ink-800">{row.label}</span>
          {row.secret && <Badge>Secret</Badge>}
          {row.restartRequired && <Badge tone="warn">Restart</Badge>}
        </div>
        <p className="mt-0.5 text-[13px] text-ink-500">{row.description}</p>
      </div>

      <div className="min-w-0">
        {row.kind === "boolean" ? (
          <Checkbox
            label={draft === "true" ? "On" : "Off"}
            checked={draft === "true"}
            disabled={disabled}
            onChange={(e) => {
              const value = e.target.checked ? "true" : "false";
              setDraft(value);
              onSave(value);
            }}
          />
        ) : row.kind === "color" ? (
          <div className="flex items-center gap-2">
            <input
              type="color"
              value={/^#[0-9a-fA-F]{6}$/.test(draft) ? draft : effective}
              disabled={disabled}
              onChange={(e) => {
                setDraft(e.target.value);
                applyColorScale(e.target.value);
                onSave(e.target.value);
              }}
              className="h-9.5 w-12 shrink-0 cursor-pointer rounded-lg border border-ink-200 bg-white p-1 disabled:cursor-not-allowed"
            />
            <TextInput
              type="text"
              value={draft}
              disabled={disabled}
              onFocus={() => setFocused(true)}
              onChange={(e) => setDraft(e.target.value)}
              onBlur={(e) => {
                if (/^#[0-9a-fA-F]{6}$/.test(e.target.value)) {
                  applyColorScale(e.target.value);
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
            value={draft}
            disabled={disabled}
            placeholder={row.kind === "string[]" ? "https://a.example.com, https://b.example.com" : row.defaultValue}
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

        {!row.secret && !row.overridden && (
          <p className="mt-1 text-xs text-ink-400">
            Default: <span className="font-mono">{formatDefault(row)}</span>
          </p>
        )}
      </div>

      <div className="sm:pt-1.5">
        {row.overridden && canEdit && (
          <button
            type="button"
            onClick={() => onSave(null)}
            className="text-[13px] font-medium whitespace-nowrap text-crimson-700 hover:underline"
          >
            Reset to default
          </button>
        )}
      </div>
    </div>
  );
}

export function SettingsPage() {
  const canEdit = useSessionCan("settings.edit");
  const queryClient = useQueryClient();
  const { data: rows, isLoading } = useQuery({ queryKey: ["settings"], queryFn: () => api.listSettings() });
  const [section, setSection] = useState<SettingSection>(SECTIONS[0]);

  const save = useMutation({
    mutationFn: ({ key, value }: { key: string; value: string | null }) => api.updateSetting(key, value),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["settings"] }),
  });

  if (isLoading) return <LoadingBlock label="Loading settings…" />;
  if (!rows) return null;

  const restartPendingCount = rows.filter((r) => r.restartRequired && r.overridden).length;
  const sectionRows = rows.filter((r) => r.section === section);

  return (
    <div>
      <PageHeader
        title="Settings"
        description="Instance-wide configuration. Defaults ship safely — override only what you need."
      />

      {restartPendingCount > 0 && (
        <div className="mb-5 flex items-center gap-2 rounded-lg border border-warn-100 bg-warn-100/60 px-4 py-2.5 text-[13px] font-medium text-warn-700">
          <TriangleAlert className="size-4 shrink-0" aria-hidden />
          Restart needed — {restartPendingCount} setting{restartPendingCount === 1 ? "" : "s"} won't take effect
          until the backend restarts.
        </div>
      )}
      <FormError>{save.error?.message}</FormError>

      <div className="mb-4 flex flex-wrap gap-2">
        {SECTIONS.map((s) => {
          const active = s === section;
          const needsRestart = rows.some((r) => r.section === s && r.restartRequired && r.overridden);
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
              {needsRestart && <span className="size-1.5 rounded-full bg-warn-700" aria-hidden />}
            </button>
          );
        })}
      </div>

      <div className="max-w-3xl rounded-xl border border-ink-200 bg-white p-5">
        <div className="divide-y divide-ink-100">
          {sectionRows.map((row) => (
            <SettingRowEditor
              key={row.key}
              row={row}
              canEdit={canEdit}
              onSave={(value) => save.mutate({ key: row.key, value })}
            />
          ))}
        </div>
      </div>
    </div>
  );
}
