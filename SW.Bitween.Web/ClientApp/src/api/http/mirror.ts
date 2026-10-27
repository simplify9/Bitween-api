import type { ApiClient } from "../client";
import type {
  MirrorBring,
  MirrorBringSection,
  MirrorListed,
  MirrorProdKinds,
  MirrorProdListAnswer,
  MirrorStatus,
  MirrorSubscriptionAnswer,
  MirrorTest,
} from "../types";
import { get, post } from "./request";

export const mirrorMethods = {
  getMirror(): Promise<MirrorStatus> {
    return get<MirrorStatus>("/mirror");
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

  /** Staging's items of one kind that prod has too, each with how its test last ran; reads only prod's list. */
  getMirrorListed(section: MirrorBringSection): Promise<MirrorListed> {
    return get<MirrorListed>(`/mirror/listed?section=${section}`);
  },

  /** Starts testing the items, one subscription after another, in the background; follow it with getMirrorListed. */
  testMirrorMany(section: MirrorBringSection, ids: number[]): Promise<{ subscriptions: string[] }> {
    return post<{ subscriptions: string[] }>("/mirror/testmany", { section, ids });
  },

  /** Drops the tests waiting their turn; the one under way finishes. */
  stopMirrorTests(): Promise<{ dropped: number }> {
    return post<{ dropped: number }>("/mirror/teststop", {});
  },

  /** What bringing the picked items from prod does; with `apply`, brings them. */
  bringFromProd(section: MirrorBringSection, keys: string[], apply: boolean): Promise<MirrorBring> {
    return post<MirrorBring>("/mirror/bring", { section, keys, apply });
  },
} satisfies Partial<ApiClient>;
