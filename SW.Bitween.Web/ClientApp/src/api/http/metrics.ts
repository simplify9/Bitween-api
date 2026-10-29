import type { ApiClient } from "../client";
import type {
  AdapterUsageDetail,
  AdapterUsageList,
  Insights,
  MetricsOverview,
  NodeMetrics,
  PartnerUsageList,
  SubscriptionHealthList,
  SubscriptionMetrics,
} from "../metricsTypes";
import { get } from "./request";

export const metricsMethods = {
  getMetricsOverview: () => get<MetricsOverview>("/metrics/overview"),
  getAdapterUsage: (days: number) => get<AdapterUsageList>(`/metrics/adapters?days=${days}`),
  getAdapterUsageDetail: (id: string, days: number) =>
    get<AdapterUsageDetail>(`/metrics/adapter?id=${encodeURIComponent(id)}&days=${days}`),
  getSubscriptionHealth: (days: number) => get<SubscriptionHealthList>(`/metrics/subscriptions?days=${days}`),
  getSubscriptionMetrics: (id: number, days: number) =>
    get<SubscriptionMetrics>(`/metrics/subscription?id=${id}&days=${days}`),
  getPartnerUsage: (days: number) => get<PartnerUsageList>(`/metrics/partners?days=${days}`),
  getNodeMetrics: () => get<NodeMetrics>("/metrics/nodes"),
  getInsights: () => get<Insights>("/metrics/insights"),
} satisfies Partial<ApiClient>;
