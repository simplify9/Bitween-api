import type { ApiClient } from "../client";
import { post } from "./request";

interface RawMapperPreviewResponse {
  outputJson: string | null;
  error: string | null;
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
} satisfies Partial<ApiClient>;
