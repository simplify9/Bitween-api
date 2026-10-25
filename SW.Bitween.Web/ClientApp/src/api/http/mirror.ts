import type { ApiClient } from "../client";
import type { MirrorCalls, MirrorCopyResult, MirrorParityAnswer, MirrorRehearsal, MirrorSamples, MirrorStatus } from "../types";
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

  getMirrorSamples(): Promise<MirrorSamples> {
    return get<MirrorSamples>("/mirror/samples");
  },

  /** The calls staging answered in place of the network, since this node started. */
  getMirrorCalls(): Promise<MirrorCalls> {
    return get<MirrorCalls>("/mirror/calls");
  },

  /** Starts taking samples of prod's exchanges in the background; follow it with getMirrorSamples. */
  takeMirrorSamples(): Promise<MirrorSamples> {
    return post<MirrorSamples>("/mirror/takesamples", {});
  },

  getMirrorRehearsal(): Promise<MirrorRehearsal> {
    return get<MirrorRehearsal>("/mirror/rehearsal");
  },

  /** Starts a rehearsal in the background: every sample run on staging, then compared with prod's; follow it with getMirrorRehearsal. */
  rehearseMirror(): Promise<MirrorRehearsal> {
    return post<MirrorRehearsal>("/mirror/rehearse", {});
  },
} satisfies Partial<ApiClient>;
