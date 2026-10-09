import type { ApiClient } from "../client";
import type { AdapterDraft, AdapterDraftSummary, DraftBuild, DraftPublishResult, DraftRun } from "../types";
import { get, post, request } from "./request";

export const adapterDraftMethods = {
  listAdapterDrafts: () => get<AdapterDraftSummary[]>("/adapterdrafts"),

  getAdapterDraft: (id: number) => get<AdapterDraft>(`/adapterdrafts/${id}`),

  createAdapterDraft: (input) => post<number>("/adapterdrafts", input),

  draftFromVersion: (adapterId: string, version: string) =>
    post<number>("/adapterdrafts", { fromAdapterId: adapterId, fromVersion: version }),

  saveAdapterDraft: (id: number, files: Record<string, string>) =>
    post<AdapterDraftSummary>(`/adapterdrafts/${id}`, { files }),

  async deleteAdapterDraft(id: number) {
    await request(`/adapterdrafts/${id}`, { method: "DELETE" });
  },

  buildAdapterDraft: (id: number, settings: Record<string, string>, buildOnly = false) =>
    post<DraftBuild>(`/adapterdrafts/${id}/build`, { settings, buildOnly }),

  tryAdapterDraft: (id: number, settings: Record<string, string>, command: string, input: string) =>
    post<DraftRun>(`/adapterdrafts/${id}/try`, { settings, command, input }),

  publishAdapterDraft: (id: number, input) => post<DraftPublishResult>(`/adapterdrafts/${id}/publish`, input),

  async promoteAdapter(adapterId: string, version: string) {
    await post("/adapters/promote", { adapterId, version });
  },
} satisfies Partial<ApiClient>;
