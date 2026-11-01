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
import { get, post, request } from "./request";

/** What a section's items are called, for the header's line about bringing them. */
const SECTION: Record<MirrorBringSection, string> = {
  subscriptions: "subscriptions",
  apiGateways: "API gateways",
  busGateways: "bus gateways",
};

export const mirrorMethods = {
  getMirror(): Promise<MirrorStatus> {
    return get<MirrorStatus>("/mirror");
  },

  /** How many of prod's items there are of each kind a page lists; reads only prod's lists. */
  getMirrorProdKinds(): Promise<MirrorProdKinds> {
    return get<MirrorProdKinds>("/mirror/prodkinds");
  },

  /**
   * Prod's items of one kind, busiest first, with whether staging has each; reads prod and staging,
   * which can take a while, so it carries on in the background when it does.
   */
  getMirrorProd(section: MirrorBringSection, type?: string): Promise<MirrorProdListAnswer> {
    return request<MirrorProdListAnswer>(`/mirror/prod?section=${section}${type ? `&type=${encodeURIComponent(type)}` : ""}`, {
      background: `Bring from prod: prod's ${SECTION[section]}`,
    });
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

  /** Says staging's own secrets are in where a bring saved placeholders on the item, which staging can't see. */
  mirrorFilledIn(section: string, key: string): Promise<{ closed: number }> {
    return post<{ closed: number }>("/mirror/filledin", { section, key });
  },

  /** What bringing the picked items from prod does; with `apply`, brings them. Carried on in the background when slow. */
  bringFromProd(section: MirrorBringSection, keys: string[], apply: boolean): Promise<MirrorBring> {
    const what = `${keys.length} ${keys.length === 1 ? SECTION[section].replace(/s$/, "") : SECTION[section]}`;
    return request<MirrorBring>("/mirror/bring", {
      method: "POST",
      body: { section, keys, apply },
      background: apply ? `Bringing ${what} from prod` : `Bring from prod: what comes with ${what}`,
    });
  },
} satisfies Partial<ApiClient>;
