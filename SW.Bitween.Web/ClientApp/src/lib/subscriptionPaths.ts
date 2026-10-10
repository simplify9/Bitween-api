import type { SubscriptionType } from "../api";
import { pathOf, type PageId } from "../pages";

/**
 * Where a subscription of each type is opened. Scheduled jobs, aggregations and response
 * subscriptions have a section of their own in the sidebar, and a subscription opened from one
 * of those lists used to land on /subscriptions/:id — highlighting "All subscriptions" and
 * offering a way back to a list it never came from.
 */
export const SUBSCRIPTION_PAGE: Record<SubscriptionType, PageId> = {
  Receiving: "scheduledJob",
  Aggregation: "aggregation",
  Response: "responseSubscription",
  GatewayApiCall: "subscription",
  BusGateway: "subscription",
  Internal: "subscription",
  ApiCall: "subscription",
};

/** The pages a subscription can be shown on. */
export const SUBSCRIPTION_PAGES = new Set<PageId>(Object.values(SUBSCRIPTION_PAGE));

/**
 * A subscription's address in its own section. Without its type, the general one: the page moves
 * itself to the section address once it knows, so a link that can't tell still lands right.
 */
export const subscriptionPath = (id: number, type?: SubscriptionType | null): string =>
  pathOf(type ? SUBSCRIPTION_PAGE[type] : "subscription", { id: String(id) })!;
