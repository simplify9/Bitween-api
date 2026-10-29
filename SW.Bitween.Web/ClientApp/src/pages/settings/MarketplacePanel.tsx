import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { KeyRound, RefreshCw, Send } from "lucide-react";
import { api, type LicenceState, type RegistryLicenceRow, type SettingRow, type UsageReportPreview } from "../../api";
import { keys } from "../../api/queryKeys";
import { Badge, Button, FormError, LoadError, LoadingBlock } from "../../components/ui/basics";
import { Field, TextInput } from "../../components/ui/forms";
import { formatDate, formatDateTime } from "../../lib/dates";

/** The catalog section the registry settings sit in. */
export const MARKETPLACE_SECTION = "Marketplace";

const STATE: Record<LicenceState, { label: string; tone: "ok" | "warn" | "danger" | "neutral" }> = {
  Live: { label: "Live", tone: "ok" },
  Grace: { label: "In grace", tone: "warn" },
  NotYet: { label: "Not yet", tone: "neutral" },
  Expired: { label: "Expired", tone: "danger" },
  None: { label: "None", tone: "neutral" },
};

function LicenceRow({ licence }: { licence: RegistryLicenceRow }) {
  return (
    <li className="flex flex-wrap items-center gap-x-3 gap-y-1 px-3 py-2 text-[13px]">
      <code className="font-mono text-ink-800">{licence.packages}</code>
      <Badge tone={STATE[licence.state].tone}>{STATE[licence.state].label}</Badge>
      <span className="text-ink-500">
        {formatDate(licence.starts)} to {formatDate(licence.ends)}
        {licence.graceDays > 0 && `, ${licence.graceDays} days of grace`}
      </span>
      <span className="ml-auto text-ink-500">
        {licence.customer}
        {licence.instances !== "*" && ` · ${licence.instances}`}
      </span>
    </li>
  );
}

/** Registering: paste the code, name this Bitween, and the token becomes a setting. */
function RegisterForm({ onDone }: { onDone: () => void }) {
  const [code, setCode] = useState("");
  const [displayName, setDisplayName] = useState("");
  const register = useMutation({
    mutationFn: () => api.registerWithRegistry(code.trim(), displayName.trim()),
    onSuccess: onDone,
  });
  return (
    <form
      className="space-y-3"
      onSubmit={(e) => {
        e.preventDefault();
        register.mutate();
      }}
    >
      <Field
        label="Registration code"
        hint="One-time, from Simplify9 or from your customer page on the registry. It becomes an instance id and a token, kept as the two settings below."
      >
        <TextInput value={code} onChange={(e) => setCode(e.target.value)} placeholder="bwreg_…" autoComplete="off" spellCheck={false} />
      </Field>
      <Field label="Call this Bitween" hint="How it appears on the registry's customer page; its address when left empty.">
        <TextInput value={displayName} onChange={(e) => setDisplayName(e.target.value)} placeholder="Production" />
      </Field>
      {register.error && <FormError>{register.error.message}</FormError>}
      <Button type="submit" variant="primary" busy={register.isPending} disabled={code.trim() === ""}>
        Register
      </Button>
    </form>
  );
}

/** Yesterday's usage report as it would go, so an administrator sees exactly what is sent before turning it on. */
function UsagePreview({ canEdit }: { canEdit: boolean }) {
  const [report, setReport] = useState<UsageReportPreview | null>(null);
  const preview = useMutation({ mutationFn: () => api.marketplaceUsage(null, false), onSuccess: setReport });
  const send = useMutation({ mutationFn: () => api.marketplaceUsage(null, true), onSuccess: setReport });
  return (
    <div className="space-y-2">
      <div className="flex flex-wrap items-center gap-2">
        <Button size="sm" busy={preview.isPending} onClick={() => preview.mutate()} disabled={!canEdit}>
          Show yesterday's report
        </Button>
        <Button size="sm" busy={send.isPending} onClick={() => send.mutate()} disabled={!canEdit}>
          <Send className="size-3.5" aria-hidden />
          Send it now
        </Button>
      </div>
      {(preview.error || send.error) && <FormError>{(preview.error ?? send.error)?.message}</FormError>}
      {report && (
        <div className="rounded-lg border border-ink-200">
          <p className="border-b border-ink-100 px-3 py-1.5 text-[12px] text-ink-500">
            {report.day}: {report.lines.length === 0 ? "nothing to say; nothing is sent on a day like this." : `${report.lines.length} connector(s).`}
          </p>
          {report.lines.length > 0 && (
            <table className="w-full text-[13px]">
              <thead className="text-left text-[11px] uppercase tracking-wide text-ink-500">
                <tr>
                  <th className="px-3 py-1.5 font-medium">Connector</th>
                  <th className="px-3 py-1.5 font-medium">Version</th>
                  <th className="px-3 py-1.5 text-right font-medium">Subscriptions</th>
                  <th className="px-3 py-1.5 text-right font-medium">Calls</th>
                  <th className="px-3 py-1.5 text-right font-medium">Failures</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-ink-100">
                {report.lines.map((l) => (
                  <tr key={`${l.packageId}@${l.version}`}>
                    <td className="px-3 py-1.5 font-mono text-ink-800">{l.packageId}</td>
                    <td className="px-3 py-1.5 text-ink-700">{l.version}</td>
                    <td className="px-3 py-1.5 text-right tabular-nums">{l.subscriptions}</td>
                    <td className="px-3 py-1.5 text-right tabular-nums">{l.calls}</td>
                    <td className="px-3 py-1.5 text-right tabular-nums">{l.failures}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
      )}
    </div>
  );
}

/**
 * Sits above the Marketplace rows: whether this Bitween is registered with the registry, the
 * licences it holds and what each one is now, and what the daily usage report would say. All of
 * it reads what was stored; Refresh asks the registry.
 */
export function MarketplacePanel({ rows, canEdit }: { rows: SettingRow[]; canEdit: boolean }) {
  const queryClient = useQueryClient();
  const enabled = rows.find((r) => r.key === "Bitween.RegistryEnabled")?.value?.toLowerCase() === "true";
  const usageOn = rows.find((r) => r.key === "Bitween.RegistryUsageReports")?.value?.toLowerCase() === "true";
  const licences = useQuery({ queryKey: keys.marketplaceLicences, queryFn: () => api.getMarketplaceLicences(), enabled });
  const refresh = useMutation({
    mutationFn: () => api.refreshMarketplaceLicences(),
    onSuccess: (summary) => queryClient.setQueryData(keys.marketplaceLicences, summary),
  });
  const registered = async () => {
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: keys.settings.all }),
      queryClient.invalidateQueries({ queryKey: keys.marketplaceLicences }),
    ]);
  };

  if (!enabled) return null;
  if (licences.isPending) return <LoadingBlock label="Reading the licences held here…" />;
  if (licences.isError) return <LoadError error={licences.error} what="the licences" onRetry={() => void licences.refetch()} />;
  const l = licences.data;

  return (
    <div className="mb-6 space-y-5 rounded-xl border border-ink-200 bg-ink-50/40 p-4">
      <div className="flex flex-wrap items-start gap-3">
        <KeyRound className="mt-0.5 size-5 shrink-0 text-ink-500" aria-hidden />
        <div className="min-w-0 flex-1">
          <h3 className="text-[13px] font-semibold text-ink-900">Registration and licences</h3>
          <p className="mt-0.5 text-[12.5px] text-ink-500">
            Free connectors never need this. A registered Bitween sees the private connectors its customer is entitled to,
            installs paid ones on a licence, and can send a daily usage report. Licences are checked here against the
            trusted keys; nothing is asked of the registry when a connector runs.
          </p>
        </div>
      </div>

      {l.registered ? (
        <>
          <div className="flex flex-wrap items-center gap-2 text-[13px] text-ink-700">
            <Badge tone="ok">Registered</Badge>
            <span>
              as <code className="font-mono">{l.instanceId}</code>
            </span>
            <span className="text-ink-500">
              {l.refreshedOn ? `Licences fetched ${formatDateTime(l.refreshedOn)}.` : "Licences not fetched yet."}
            </span>
            <Button size="sm" className="ml-auto" busy={refresh.isPending} onClick={() => refresh.mutate()}>
              <RefreshCw className="size-3.5" aria-hidden />
              Refresh
            </Button>
          </div>
          {(l.error || refresh.error) && <FormError>{refresh.error?.message ?? l.error}</FormError>}
          {l.licences.length === 0 ? (
            <p className="text-[13px] text-ink-500">No licences held. Ask Simplify9 for one; it appears here after the next refresh.</p>
          ) : (
            <ul className="divide-y divide-ink-100 rounded-lg border border-ink-200 bg-white">
              {l.licences.map((licence) => (
                <LicenceRow key={licence.licenceId} licence={licence} />
              ))}
            </ul>
          )}
          <div>
            <h4 className="text-[13px] font-medium text-ink-800">Usage report {usageOn ? "(on, daily)" : "(off)"}</h4>
            <p className="mt-0.5 mb-2 text-[12.5px] text-ink-500">
              Per connector installed from the registry: subscriptions using it, calls and failures. No payloads, no names, no
              hostnames. Turn it on with the setting below.
            </p>
            <UsagePreview canEdit={canEdit} />
          </div>
          <p className="text-[12px] text-ink-500">To unregister, clear the two settings below.</p>
        </>
      ) : canEdit ? (
        <RegisterForm onDone={registered} />
      ) : (
        <p className="text-[13px] text-ink-500">Not registered. An administrator with Settings can register it with a code from Simplify9.</p>
      )}
    </div>
  );
}
