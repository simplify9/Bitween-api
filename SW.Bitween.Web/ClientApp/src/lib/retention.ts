import { useEffect, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { api, type RetentionProposal, type SettingRow } from "../api";
import { keys } from "../api/queryKeys";
import type { SettingsDraft } from "./settingsDraft";

/** The settings whose effect on retention the panel explains, and the proposal field each feeds. */
export const RETENTION_KEYS = {
  documentPrefix: "Bitween.DocumentPrefix",
  retentionDays: "Bitween.ExchangeRetentionDays",
  archive: "Bitween.ArchiveExchanges",
  cron: "Bitween.ExchangeRetentionCron",
  publicUrl: "Bitween.PublicUrl",
} as const;

export const RETENTION_SECTION = "Documents & storage";

/** Staged changes to the retention settings, as a proposal; null when none are staged. */
export function retentionProposal(rows: SettingRow[], draft: SettingsDraft): RetentionProposal | null {
  const staged = Object.values(RETENTION_KEYS).some((key) => key in draft);
  if (!staged) return null;

  // A staged reset (null) means "back to the default", which is what the server would store.
  const valueOf = (key: string) => {
    const row = rows.find((r) => r.key === key);
    const value = key in draft ? draft[key] : row?.value;
    return value ?? row?.defaultValue ?? "";
  };
  const days = Number(valueOf(RETENTION_KEYS.retentionDays));

  return {
    documentPrefix: valueOf(RETENTION_KEYS.documentPrefix),
    exchangeRetentionDays: Number.isFinite(days) ? days : undefined,
    archiveExchanges: valueOf(RETENTION_KEYS.archive) === "true",
    exchangeRetentionCron: valueOf(RETENTION_KEYS.cron),
    publicUrl: valueOf(RETENTION_KEYS.publicUrl),
  };
}

/**
 * The saved retention status, or — while retention settings are staged — what they would do. Only
 * asked for while `wanted`: the counts behind it touch the exchange table, so pages that aren't
 * showing it shouldn't pay for it.
 */
export function useRetention(rows: SettingRow[], draft: SettingsDraft, wanted = true) {
  const proposal = retentionProposal(rows, draft);
  // Typing a number stages it on blur, but a pasted value can change in quick succession; waiting a
  // beat keeps each edit from costing a round of counts on the exchange table.
  const [settled, setSettled] = useState(proposal);
  const proposalKey = JSON.stringify(proposal);
  useEffect(() => {
    const timer = setTimeout(() => setSettled(proposal), 300);
    return () => clearTimeout(timer);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [proposalKey]);

  const saved = useQuery({ queryKey: keys.settings.retention, queryFn: () => api.getRetention(), enabled: wanted });
  const preview = useQuery({
    queryKey: keys.settings.retentionPreview(JSON.stringify(settled)),
    queryFn: () => api.previewRetention(settled!),
    enabled: wanted && settled !== null,
    placeholderData: (previous) => previous,
  });

  return {
    status: proposal ? (preview.data ?? saved.data) : saved.data,
    previewing: proposal !== null,
    loading: saved.isLoading || (proposal !== null && preview.isFetching),
    error: (proposal ? preview.error : null) ?? saved.error,
  };
}

