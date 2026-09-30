import type { ReactNode } from "react";
import type { RetryAlertConfig, RetryAlertMode } from "../../api";
import { NotificationChannelPicker } from "../../components/config/pickers";

/**
 * Where a "retry budget ran out" alert goes, as set at one level of the hierarchy.
 *
 * Three levels decide between them — the policy, a group, and one subscription-and-group pair —
 * resolved most specific first. A level that sends **replaces** the level above: it names the
 * one notification channel the alert goes through. Channels carry their own delivery settings,
 * set up once in Settings, so a level only has to say which.
 */

const MODES: { value: RetryAlertMode; label: string; hint: string }[] = [
  { value: "Inherit", label: "Inherit", hint: "Use whatever the level above sends." },
  { value: "Send", label: "Send here", hint: "Send through this level's own channel instead." },
  { value: "Silent", label: "Silent", hint: "Send nothing, even if a level above would." },
];

export function AlertRouting({
  value,
  onChange,
  inherited,
  disabled = false,
}: {
  value: RetryAlertConfig;
  onChange: (next: RetryAlertConfig) => void;
  /**
   * What Inherit resolves to right now, spelled out — "Inherit" alone tells you the rule but
   * not the outcome, and the outcome is the thing being decided.
   */
  inherited: ReactNode;
  disabled?: boolean;
}) {
  const setMode = (mode: RetryAlertMode) => {
    // Leaving Send keeps the channel in state but stops sending to it, so flipping to Silent to
    // hush an alert overnight and back again doesn't cost you the configuration.
    if (mode === "Send") return onChange({ ...value, alertMode: "Send" });
    onChange({ ...value, alertMode: mode });
  };

  return (
    <div className="space-y-3">
      <div role="radiogroup" aria-label="Alert routing" className="flex flex-wrap gap-1.5">
        {MODES.map((m) => {
          const active = value.alertMode === m.value;
          return (
            <button
              key={m.value}
              type="button"
              role="radio"
              aria-checked={active}
              disabled={disabled}
              onClick={() => setMode(m.value)}
              className={`rounded-lg px-3 py-1.5 text-[13px] font-medium transition-colors disabled:opacity-60 ${
                active
                  ? "bg-ink-900 text-white"
                  : "bg-ink-50 text-ink-600 hover:bg-ink-100 hover:text-ink-900"
              }`}
            >
              {m.label}
            </button>
          );
        })}
      </div>

      <p className="text-[13px] text-ink-500">
        {value.alertMode === "Inherit" ? inherited : MODES.find((m) => m.value === value.alertMode)!.hint}
      </p>

      {value.alertMode === "Send" && (
        <div className="max-w-sm">
          <NotificationChannelPicker
            value={value.alertChannelId}
            disabled={disabled}
            onChange={(alertChannelId) => onChange({ ...value, alertChannelId })}
          />
        </div>
      )}
    </div>
  );
}
