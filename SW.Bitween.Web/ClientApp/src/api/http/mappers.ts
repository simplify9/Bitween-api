import type { ApiClient } from "../client";
import { post, request } from "./request";

interface RawMapperPreviewResponse {
  outputJson: string | null;
  error: string | null;
}

interface RawMappingPreviewResponse {
  outputDocument: string | null;
  contentType: string | null;
  ruleErrors: { target: string; reason: string }[] | null;
  error: string | null;
}

interface RawMappingSuggestionResponse extends RawMappingPreviewResponse {
  mappingRules: string;
  differences: { target: string; reason: string }[] | null;
  matchesTarget: boolean;
  model: string | null;
}

export const mapperMethods = {
  async previewMapping(input: {
    scribanTemplate: string;
    inputJson: string;
    partnerId?: number | null;
  }): Promise<{ outputJson: string | null; error: string | null }> {
    const res = await post<RawMapperPreviewResponse>("/mappers", input);
    return { outputJson: res.outputJson ?? null, error: res.error ?? null };
  },

  async previewMappingRules(input: {
    mappingRules: string;
    sourceDocument: string;
    partnerId?: number | null;
  }): Promise<{
    outputDocument: string | null;
    contentType: string | null;
    ruleErrors: { target: string; reason: string }[];
    error: string | null;
  }> {
    const res = await post<RawMappingPreviewResponse>("/mappingpreviews", input);
    return {
      outputDocument: res.outputDocument ?? null,
      contentType: res.contentType ?? null,
      ruleErrors: res.ruleErrors ?? [],
      error: res.error ?? null,
    };
  },

  async suggestMappingRules(
    input: {
      mappingRules: string;
      sourceDocument: string;
      targetDocument: string;
      partnerId?: number | null;
      refine?: boolean;
      instructions?: string;
    },
    signal?: AbortSignal,
  ): Promise<{
    mappingRules: string;
    differences: { target: string; reason: string }[];
    matchesTarget: boolean;
    model: string | null;
  }> {
    const res = await request<RawMappingSuggestionResponse>("/mappingsuggestions", {
      method: "POST",
      body: input,
      signal,
    });
    return {
      mappingRules: res.mappingRules,
      differences: res.differences ?? [],
      matchesTarget: res.matchesTarget,
      model: res.model ?? null,
    };
  },
} satisfies Partial<ApiClient>;
