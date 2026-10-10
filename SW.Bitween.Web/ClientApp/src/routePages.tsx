import { lazy } from "react";
import { Navigate } from "react-router";
import { useSession } from "./auth/useSession";
import { homePath } from "./nav";

/*
 * The router's pages, kept apart from the route table: a module that exports components and
 * something else besides (the table, the router) can't be hot-reloaded.
 */

// Every page but sign-in loads when it is first opened, so the first screen doesn't wait for all of
// them: the mapper editors and CodeMirror alone were most of a 1.8 MB bundle. AppShell's Suspense
// shows a loading line meanwhile.
export const AdapterEditorPage = lazy(() => import("./pages/adapters/AdapterEditorPage"));
export const AdaptersPage = lazy(() => import("./pages/adapters/AdaptersPage").then((m) => ({ default: m.AdaptersPage })));
export const ProfilePage = lazy(() => import("./pages/ProfilePage").then((m) => ({ default: m.ProfilePage })));
export const AuditPage = lazy(() => import("./pages/audit/AuditPage").then((m) => ({ default: m.AuditPage })));
export const NodesPage = lazy(() => import("./pages/nodes/NodesPage").then((m) => ({ default: m.NodesPage })));
export const SettingsPage = lazy(() => import("./pages/settings/SettingsPage").then((m) => ({ default: m.SettingsPage })));
export const DashboardPage = lazy(() => import("./pages/dashboard/DashboardPage").then((m) => ({ default: m.DashboardPage })));
export const ExchangeNewPage = lazy(() => import("./pages/exchanges/ExchangeNewPage").then((m) => ({ default: m.ExchangeNewPage })));
export const ExchangesPage = lazy(() => import("./pages/exchanges/ExchangesPage").then((m) => ({ default: m.ExchangesPage })));
export const QueueHealthPage = lazy(() => import("./pages/queue-health/QueueHealthPage").then((m) => ({ default: m.QueueHealthPage })));
export const ScheduledRetriesPage = lazy(() => import("./pages/scheduled-retries/ScheduledRetriesPage").then((m) => ({ default: m.ScheduledRetriesPage })));
export const ApiGatewayNewPage = lazy(() => import("./pages/api-gateways/ApiGatewayNewPage").then((m) => ({ default: m.ApiGatewayNewPage })));
export const ApiGatewaysPage = lazy(() => import("./pages/api-gateways/ApiGatewaysPage").then((m) => ({ default: m.ApiGatewaysPage })));
export const ApiGatewayPage = lazy(() => import("./pages/api-gateways/ApiGatewayPage").then((m) => ({ default: m.ApiGatewayPage })));
export const AttachPartnerPage = lazy(() => import("./pages/api-gateways/AttachPartnerPage").then((m) => ({ default: m.AttachPartnerPage })));
export const NewGatewaySubscriptionPage = lazy(() => import("./pages/api-gateways/NewGatewaySubscriptionPage").then((m) => ({ default: m.NewGatewaySubscriptionPage })));
export const EditAttachmentPage = lazy(() => import("./pages/api-gateways/EditAttachmentPage").then((m) => ({ default: m.EditAttachmentPage })));
export const BusGatewayNewPage = lazy(() => import("./pages/bus-gateways/BusGatewayNewPage").then((m) => ({ default: m.BusGatewayNewPage })));
export const BusGatewayPage = lazy(() => import("./pages/bus-gateways/BusGatewayPage").then((m) => ({ default: m.BusGatewayPage })));
export const BusGatewaysPage = lazy(() => import("./pages/bus-gateways/BusGatewaysPage").then((m) => ({ default: m.BusGatewaysPage })));
export const DataSourceNewPage = lazy(() => import("./pages/data-sources/DataSourceNewPage").then((m) => ({ default: m.DataSourceNewPage })));
export const DataSourcePage = lazy(() => import("./pages/data-sources/DataSourcePage").then((m) => ({ default: m.DataSourcePage })));
export const DataSourcesPage = lazy(() => import("./pages/data-sources/DataSourcesPage").then((m) => ({ default: m.DataSourcesPage })));
export const FlowPage = lazy(() => import("./pages/flow/FlowPage").then((m) => ({ default: m.FlowPage })));
export const GlobalValueSetPage = lazy(() => import("./pages/global-values/GlobalValueSetPage").then((m) => ({ default: m.GlobalValueSetPage })));
export const GlobalValueSetsPage = lazy(() => import("./pages/global-values/GlobalValueSetsPage").then((m) => ({ default: m.GlobalValueSetsPage })));
export const InformationTypePage = lazy(() => import("./pages/information-types/InformationTypePage").then((m) => ({ default: m.InformationTypePage })));
export const InformationTypesPage = lazy(() => import("./pages/information-types/InformationTypesPage").then((m) => ({ default: m.InformationTypesPage })));
export const SubscriptionPage = lazy(() => import("./pages/subscriptions/SubscriptionPage").then((m) => ({ default: m.SubscriptionPage })));
export const SubscriptionsPage = lazy(() => import("./pages/subscriptions/SubscriptionsPage").then((m) => ({ default: m.SubscriptionsPage })));
export const NotifierPage = lazy(() => import("./pages/notifiers/NotifierPage").then((m) => ({ default: m.NotifierPage })));
export const NotifiersPage = lazy(() => import("./pages/notifiers/NotifiersPage").then((m) => ({ default: m.NotifiersPage })));
export const PartnerPage = lazy(() => import("./pages/partners/PartnerPage").then((m) => ({ default: m.PartnerPage })));
export const PartnersPage = lazy(() => import("./pages/partners/PartnersPage").then((m) => ({ default: m.PartnersPage })));
export const NewScheduledJobPage = lazy(() => import("./pages/scheduled-jobs/NewScheduledJobPage").then((m) => ({ default: m.NewScheduledJobPage })));
export const ScheduledJobsPage = lazy(() => import("./pages/scheduled-jobs/ScheduledJobsPage").then((m) => ({ default: m.ScheduledJobsPage })));
export const AggregationsPage = lazy(() => import("./pages/aggregations/AggregationsPage").then((m) => ({ default: m.AggregationsPage })));
export const NewAggregationPage = lazy(() => import("./pages/aggregations/NewAggregationPage").then((m) => ({ default: m.NewAggregationPage })));
export const ResponseSubscriptionsPage = lazy(() => import("./pages/response-subscriptions/ResponseSubscriptionsPage").then((m) => ({ default: m.ResponseSubscriptionsPage })));
export const NewResponseSubscriptionPage = lazy(() => import("./pages/response-subscriptions/NewResponseSubscriptionPage").then((m) => ({ default: m.NewResponseSubscriptionPage })));
export const RetryPoliciesPage = lazy(() => import("./pages/retry-policies/RetryPoliciesPage").then((m) => ({ default: m.RetryPoliciesPage })));
export const RetryPolicyPage = lazy(() => import("./pages/retry-policies/RetryPolicyPage").then((m) => ({ default: m.RetryPolicyPage })));
export const MembersPage = lazy(() => import("./pages/team/MembersPage").then((m) => ({ default: m.MembersPage })));
export const RoleEditor = lazy(() => import("./pages/team/RoleEditor").then((m) => ({ default: m.RoleEditor })));
export const RolesPage = lazy(() => import("./pages/team/RolesPage").then((m) => ({ default: m.RolesPage })));
export const WorkGroupPage = lazy(() => import("./pages/work-groups/WorkGroupPage").then((m) => ({ default: m.WorkGroupPage })));
export const WorkGroupsPage = lazy(() => import("./pages/work-groups/WorkGroupsPage").then((m) => ({ default: m.WorkGroupsPage })));
export const MapperEditorRoute = lazy(() => import("./components/nativeMapper/MapperEditorRoute"));

/** "/" lands on the first page this session is allowed to see. */
export function HomeRedirect() {
  const { session } = useSession();
  if (!session) return null; // RequireAuth already handled this
  return <Navigate to={homePath(session)} replace />;
}

/** An old "/team" link lands on whichever of the two pages this session can open. */
export function TeamRedirect() {
  const { can } = useSession();
  return <Navigate to={can("users.view") ? "/team/members" : "/team/roles"} replace />;
}
