import type { ApiClient } from "../client";
import type { MirrorCopyResult, MirrorParityAnswer, MirrorStatus } from "../types";
import { get, post } from "./request";

export const mirrorMethods = {
  getMirror(): Promise<MirrorStatus> {
    return get<MirrorStatus>("/mirror");
  },

  /** Reads prod and staging now, which takes a few seconds; prod is only read. */
  getMirrorParity(): Promise<MirrorParityAnswer> {
    return get<MirrorParityAnswer>("/mirror/parity");
  },

  /** Copies prod's setup onto this staging, faked; `removeExtras` also removes what prod doesn't have. */
  copyFromProd(removeExtras: boolean): Promise<MirrorCopyResult> {
    return post<MirrorCopyResult>("/mirror/copy", { removeExtras });
  },
} satisfies Partial<ApiClient>;
