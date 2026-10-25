import type { ApiClient } from "../client";
import type {
  MirrorBring,
  MirrorBringSection,
  MirrorCalls,
  MirrorCopyResult,
  MirrorParityAnswer,
  MirrorProdKinds,
  MirrorProdListAnswer,
  MirrorRehearsal,
  MirrorSamples,
  MirrorStatus,
  MirrorSubscriptionAnswer,
  MirrorTest,
} from "../types";
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

  /** How many of prod's items there are of each kind a page lists; reads only prod's lists. */
  getMirrorProdKinds(): Promise<MirrorProdKinds> {
    return get<MirrorProdKinds>("/mirror/prodkinds");
  },

  /** Prod's items of one kind, busiest first, with whether staging has each; reads prod and staging, a few seconds. */
  getMirrorProd(section: MirrorBringSection, type?: string): Promise<MirrorProdListAnswer> {
    return get<MirrorProdListAnswer>(`/mirror/prod?section=${section}${type ? `&type=${encodeURIComponent(type)}` : ""}`);
  },

  /** One of staging's subscriptions beside prod's; reads prod and staging, a few seconds. */
  getMirrorSubscription(id: number): Promise<MirrorSubscriptionAnswer> {
    return get<MirrorSubscriptionAnswer>(`/mirror/subscription?id=${id}`);
  },

  getMirrorTest(id: number): Promise<MirrorTest> {
    return get<MirrorTest>(`/mirror/test?id=${id}`);
  },

  /** Starts taking prod's latest arrivals of the subscription in the background; follow it with getMirrorTest. */
  takeMirrorTest(id: number): Promise<MirrorTest> {
    return post<MirrorTest>("/mirror/testtake", { id });
  },

  /** Starts the subscription's test in the background; follow it with getMirrorTest. */
  runMirrorTest(id: number): Promise<MirrorTest> {
    return post<MirrorTest>("/mirror/testrun", { id });
  },

  /** Removes what the subscription's test made on staging. */
  clearMirrorTest(id: number): Promise<MirrorTest> {
    return post<MirrorTest>("/mirror/testclear", { id });
  },

  /** What bringing the picked items from prod does; with `apply`, brings them. */
  bringFromProd(section: MirrorBringSection, keys: string[], apply: boolean): Promise<MirrorBring> {
    return post<MirrorBring>("/mirror/bring", { section, keys, apply });
  },
} satisfies Partial<ApiClient>;
