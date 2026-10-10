import { Navigate, createBrowserRouter, type RouteObject } from "react-router";
import { RequireAuth, RequirePermission } from "./auth/guards";
import { useSession } from "./auth/SessionContext";
import { AppShell } from "./components/layout/AppShell";
import { homePath } from "./nav";
import { LoginPage } from "./pages/auth/Login";
import { ChangePasswordPage } from "./pages/auth/ChangePassword";
import { CliLoginPage } from "./pages/auth/CliLogin";
import { NotFoundPage } from "./pages/NotFoundPage";
import { RouteError } from "./pages/RouteError";
import { lazy, Suspense } from "react";
import { LoadingBlock } from "./components/ui/basics";

// Every page but sign-in loads when it is first opened, so the first screen doesn't wait for all of
// them: the mapper editors and CodeMirror alone were most of a 1.8 MB bundle. AppShell's Suspense
// shows a loading line meanwhile.
const AdapterEditorPage = lazy(() => import("./pages/adapters/AdapterEditorPage"));
const AdaptersPage = lazy(() => import("./pages/adapters/AdaptersPage").then((m) => ({ default: m.AdaptersPage })));
const ProfilePage = lazy(() => import("./pages/ProfilePage").then((m) => ({ default: m.ProfilePage })));
const AuditPage = lazy(() => import("./pages/audit/AuditPage").then((m) => ({ default: m.AuditPage })));
const NodesPage = lazy(() => import("./pages/nodes/NodesPage").then((m) => ({ default: m.NodesPage })));
const SettingsPage = lazy(() => import("./pages/settings/SettingsPage").then((m) => ({ default: m.SettingsPage })));
const DashboardPage = lazy(() => import("./pages/dashboard/DashboardPage").then((m) => ({ default: m.DashboardPage })));
const ExchangeNewPage = lazy(() => import("./pages/exchanges/ExchangeNewPage").then((m) => ({ default: m.ExchangeNewPage })));
const ExchangesPage = lazy(() => import("./pages/exchanges/ExchangesPage").then((m) => ({ default: m.ExchangesPage })));
const QueueHealthPage = lazy(() => import("./pages/queue-health/QueueHealthPage").then((m) => ({ default: m.QueueHealthPage })));
const ScheduledRetriesPage = lazy(() => import("./pages/scheduled-retries/ScheduledRetriesPage").then((m) => ({ default: m.ScheduledRetriesPage })));
const ApiGatewayNewPage = lazy(() => import("./pages/api-gateways/ApiGatewayNewPage").then((m) => ({ default: m.ApiGatewayNewPage })));
const ApiGatewaysPage = lazy(() => import("./pages/api-gateways/ApiGatewaysPage").then((m) => ({ default: m.ApiGatewaysPage })));
const ApiGatewayPage = lazy(() => import("./pages/api-gateways/ApiGatewayPage").then((m) => ({ default: m.ApiGatewayPage })));
const AttachPartnerPage = lazy(() => import("./pages/api-gateways/AttachPartnerPage").then((m) => ({ default: m.AttachPartnerPage })));
const NewGatewaySubscriptionPage = lazy(() => import("./pages/api-gateways/NewGatewaySubscriptionPage").then((m) => ({ default: m.NewGatewaySubscriptionPage })));
const EditAttachmentPage = lazy(() => import("./pages/api-gateways/EditAttachmentPage").then((m) => ({ default: m.EditAttachmentPage })));
const BusGatewayNewPage = lazy(() => import("./pages/bus-gateways/BusGatewayNewPage").then((m) => ({ default: m.BusGatewayNewPage })));
const BusGatewayPage = lazy(() => import("./pages/bus-gateways/BusGatewayPage").then((m) => ({ default: m.BusGatewayPage })));
const BusGatewaysPage = lazy(() => import("./pages/bus-gateways/BusGatewaysPage").then((m) => ({ default: m.BusGatewaysPage })));
const DataSourceNewPage = lazy(() => import("./pages/data-sources/DataSourceNewPage").then((m) => ({ default: m.DataSourceNewPage })));
const DataSourcePage = lazy(() => import("./pages/data-sources/DataSourcePage").then((m) => ({ default: m.DataSourcePage })));
const DataSourcesPage = lazy(() => import("./pages/data-sources/DataSourcesPage").then((m) => ({ default: m.DataSourcesPage })));
const FlowPage = lazy(() => import("./pages/flow/FlowPage").then((m) => ({ default: m.FlowPage })));
const GlobalValueSetPage = lazy(() => import("./pages/global-values/GlobalValueSetPage").then((m) => ({ default: m.GlobalValueSetPage })));
const GlobalValueSetsPage = lazy(() => import("./pages/global-values/GlobalValueSetsPage").then((m) => ({ default: m.GlobalValueSetsPage })));
const InformationTypePage = lazy(() => import("./pages/information-types/InformationTypePage").then((m) => ({ default: m.InformationTypePage })));
const InformationTypesPage = lazy(() => import("./pages/information-types/InformationTypesPage").then((m) => ({ default: m.InformationTypesPage })));
const SubscriptionPage = lazy(() => import("./pages/subscriptions/SubscriptionPage").then((m) => ({ default: m.SubscriptionPage })));
const SubscriptionsPage = lazy(() => import("./pages/subscriptions/SubscriptionsPage").then((m) => ({ default: m.SubscriptionsPage })));
const NotifierPage = lazy(() => import("./pages/notifiers/NotifierPage").then((m) => ({ default: m.NotifierPage })));
const NotifiersPage = lazy(() => import("./pages/notifiers/NotifiersPage").then((m) => ({ default: m.NotifiersPage })));
const PartnerPage = lazy(() => import("./pages/partners/PartnerPage").then((m) => ({ default: m.PartnerPage })));
const PartnersPage = lazy(() => import("./pages/partners/PartnersPage").then((m) => ({ default: m.PartnersPage })));
const NewScheduledJobPage = lazy(() => import("./pages/scheduled-jobs/NewScheduledJobPage").then((m) => ({ default: m.NewScheduledJobPage })));
const ScheduledJobsPage = lazy(() => import("./pages/scheduled-jobs/ScheduledJobsPage").then((m) => ({ default: m.ScheduledJobsPage })));
const AggregationsPage = lazy(() => import("./pages/aggregations/AggregationsPage").then((m) => ({ default: m.AggregationsPage })));
const NewAggregationPage = lazy(() => import("./pages/aggregations/NewAggregationPage").then((m) => ({ default: m.NewAggregationPage })));
const ResponseSubscriptionsPage = lazy(() => import("./pages/response-subscriptions/ResponseSubscriptionsPage").then((m) => ({ default: m.ResponseSubscriptionsPage })));
const NewResponseSubscriptionPage = lazy(() => import("./pages/response-subscriptions/NewResponseSubscriptionPage").then((m) => ({ default: m.NewResponseSubscriptionPage })));
const RetryPoliciesPage = lazy(() => import("./pages/retry-policies/RetryPoliciesPage").then((m) => ({ default: m.RetryPoliciesPage })));
const RetryPolicyPage = lazy(() => import("./pages/retry-policies/RetryPolicyPage").then((m) => ({ default: m.RetryPolicyPage })));
const MembersPage = lazy(() => import("./pages/team/MembersPage").then((m) => ({ default: m.MembersPage })));
const RoleEditor = lazy(() => import("./pages/team/RoleEditor").then((m) => ({ default: m.RoleEditor })));
const RolesPage = lazy(() => import("./pages/team/RolesPage").then((m) => ({ default: m.RolesPage })));
const WorkGroupPage = lazy(() => import("./pages/work-groups/WorkGroupPage").then((m) => ({ default: m.WorkGroupPage })));
const WorkGroupsPage = lazy(() => import("./pages/work-groups/WorkGroupsPage").then((m) => ({ default: m.WorkGroupsPage })));
const MapperEditorRoute = lazy(() => import("./components/nativeMapper/MapperEditorRoute"));

/** "/" lands on the first page this session is allowed to see. */
function HomeRedirect() {
  const { session } = useSession();
  if (!session) return null; // RequireAuth already handled this
  return <Navigate to={homePath(session)} replace />;
}

/** An old "/team" link lands on whichever of the two pages this session can open. */
function TeamRedirect() {
  const { can } = useSession();
  return <Navigate to={can("users.view") ? "/team/members" : "/team/roles"} replace />;
}

/** "/" → undefined (no basename); "/prefix/" → "/prefix" if ever remounted. */
const basename = import.meta.env.BASE_URL.replace(/\/+$/, "") || undefined;

/** Exported apart from the router so the component tests can mount the same routes in memory. */
export const routes: RouteObject[] = [
  { path: "/login", element: <LoginPage />, errorElement: <RouteError /> },
  {
    element: <RequireAuth />,
    // A page that crashes shows this, with a way back, rather than react-router's developer screen.
    errorElement: <RouteError />,
    children: [
      // Inside the auth guard because it needs a session, outside the shell because the session
      // it serves grants nothing — a sidebar built from those permissions would be empty.
      { path: "change-password", element: <ChangePasswordPage /> },
      // Where bitween login sends the browser; a session is all it needs, whatever it may do.
      { path: "cli-login", element: <CliLoginPage /> },
      {
        element: <AppShell />,
        children: [
          { index: true, element: <HomeRedirect /> },
          {
            // No sidebar entry — the logo links here instead.
            path: "dashboard",
            element: (
              <RequirePermission permission="dashboard.view">
                <DashboardPage />
              </RequirePermission>
            ),
          },
          {
            path: "exchanges",
            element: (
              <RequirePermission permission="exchanges.view">
                <ExchangesPage />
              </RequirePermission>
            ),
          },
          {
            path: "exchanges/new",
            element: (
              <RequirePermission permission="exchanges.operate">
                <ExchangeNewPage />
              </RequirePermission>
            ),
          },
          {
            path: "scheduled-retries",
            element: (
              <RequirePermission permission="exchanges.view">
                <ScheduledRetriesPage />
              </RequirePermission>
            ),
          },
          {
            path: "queue-health",
            element: (
              <RequirePermission permission="monitoring.view">
                <QueueHealthPage />
              </RequirePermission>
            ),
          },
          // Members and Roles are two sidebar entries, not tabs inside a "Team" page, so
          // "/team" itself is no longer a page — only a bookmark people may still hold.
          { path: "team", element: <TeamRedirect /> },
          {
            path: "team/members",
            element: (
              <RequirePermission permission="users.view">
                <MembersPage />
              </RequirePermission>
            ),
          },
          {
            path: "team/members/:id",
            element: (
              <RequirePermission permission="users.view">
                <MembersPage />
              </RequirePermission>
            ),
          },
          {
            path: "team/roles",
            element: (
              <RequirePermission permission="roles.view">
                <RolesPage />
              </RequirePermission>
            ),
          },
          {
            path: "team/roles/new",
            element: (
              <RequirePermission permission="roles.create">
                <RoleEditor />
              </RequirePermission>
            ),
          },
          {
            path: "team/roles/:id",
            element: (
              <RequirePermission permission="roles.view">
                <RoleEditor />
              </RequirePermission>
            ),
          },
          { path: "profile", element: <ProfilePage /> },
          {
            path: "subscriptions",
            element: (
              <RequirePermission permission="subscriptions.view">
                <SubscriptionsPage />
              </RequirePermission>
            ),
          },
          {
            path: "subscriptions/:id",
            element: (
              <RequirePermission permission="subscriptions.view">
                <SubscriptionPage />
              </RequirePermission>
            ),
          },
          {
            path: "subscriptions/:id/mapper",
            element: (
              <RequirePermission permission="subscriptions.edit">
                <MapperEditorRoute />
              </RequirePermission>
            ),
          },
          {
            path: "api-gateways",
            element: (
              <RequirePermission permission="api-gateways.view">
                <ApiGatewaysPage />
              </RequirePermission>
            ),
          },
          {
            path: "bus-gateways",
            element: (
              <RequirePermission permission="bus-gateways.view">
                <BusGatewaysPage />
              </RequirePermission>
            ),
          },
          {
            path: "data-sources",
            element: (
              <RequirePermission permission="data-sources.view">
                <DataSourcesPage />
              </RequirePermission>
            ),
          },
          {
            path: "flow",
            element: (
              <RequirePermission permission="bus-gateways.view">
                <FlowPage />
              </RequirePermission>
            ),
          },
          {
            path: "scheduled-jobs",
            element: (
              <RequirePermission permission="subscriptions.view">
                <ScheduledJobsPage />
              </RequirePermission>
            ),
          },
          {
            path: "aggregations",
            element: (
              <RequirePermission permission="subscriptions.view">
                <AggregationsPage />
              </RequirePermission>
            ),
          },
          {
            path: "aggregations/new",
            element: (
              <RequirePermission permission="subscriptions.create">
                <NewAggregationPage />
              </RequirePermission>
            ),
          },
          {
            path: "response-subscriptions",
            element: (
              <RequirePermission permission="subscriptions.view">
                <ResponseSubscriptionsPage />
              </RequirePermission>
            ),
          },
          {
            path: "response-subscriptions/new",
            element: (
              <RequirePermission permission="subscriptions.create">
                <NewResponseSubscriptionPage />
              </RequirePermission>
            ),
          },
          {
            path: "api-gateways/new",
            element: (
              <RequirePermission permission="api-gateways.create">
                <ApiGatewayNewPage />
              </RequirePermission>
            ),
          },
          {
            path: "api-gateways/:id",
            element: (
              <RequirePermission permission="api-gateways.view">
                <ApiGatewayPage />
              </RequirePermission>
            ),
          },
          {
            path: "api-gateways/:id/attach",
            element: (
              <RequirePermission permission="api-gateways.edit">
                <AttachPartnerPage />
              </RequirePermission>
            ),
          },
          {
            path: "api-gateways/:id/attach/new-subscription",
            element: (
              <RequirePermission permission="subscriptions.create">
                <NewGatewaySubscriptionPage />
              </RequirePermission>
            ),
          },
          {
            path: "api-gateways/:id/attachments/:partnerId",
            element: (
              <RequirePermission permission="api-gateways.edit">
                <EditAttachmentPage />
              </RequirePermission>
            ),
          },
          {
            path: "bus-gateways/new",
            element: (
              <RequirePermission permission="bus-gateways.create">
                <BusGatewayNewPage />
              </RequirePermission>
            ),
          },
          {
            path: "bus-gateways/:id",
            element: (
              <RequirePermission permission="bus-gateways.view">
                <BusGatewayPage />
              </RequirePermission>
            ),
          },
          {
            // Before the :id route, or "new" is read as an id.
            path: "data-sources/new",
            element: (
              <RequirePermission permission="data-sources.create">
                <DataSourceNewPage />
              </RequirePermission>
            ),
          },
          {
            path: "data-sources/:id",
            element: (
              <RequirePermission permission="data-sources.view">
                <DataSourcePage />
              </RequirePermission>
            ),
          },
          {
            path: "scheduled-jobs/new",
            element: (
              <RequirePermission permission="subscriptions.create">
                <NewScheduledJobPage />
              </RequirePermission>
            ),
          },
          {
            path: "partners",
            element: (
              <RequirePermission permission="partners.view">
                <PartnersPage />
              </RequirePermission>
            ),
          },
          {
            path: "partners/:id",
            element: (
              <RequirePermission permission="partners.view">
                <PartnerPage />
              </RequirePermission>
            ),
          },
          {
            path: "adapters",
            element: (
              <RequirePermission permission="subscriptions.view">
                <AdaptersPage />
              </RequirePermission>
            ),
          },
          {
            path: "adapters/drafts/:id",
            element: (
              <RequirePermission permission="adapter-source.edit">
                <Suspense fallback={<LoadingBlock label="Opening the editor…" />}>
                  <AdapterEditorPage />
                </Suspense>
              </RequirePermission>
            ),
          },
          {
            path: "information-types",
            element: (
              <RequirePermission permission="documents.view">
                <InformationTypesPage />
              </RequirePermission>
            ),
          },
          {
            path: "information-types/:id",
            element: (
              <RequirePermission permission="documents.view">
                <InformationTypePage />
              </RequirePermission>
            ),
          },
          {
            path: "global-values",
            element: (
              <RequirePermission permission="global-values.view">
                <GlobalValueSetsPage />
              </RequirePermission>
            ),
          },
          {
            path: "global-values/:id",
            element: (
              <RequirePermission permission="global-values.view">
                <GlobalValueSetPage />
              </RequirePermission>
            ),
          },
          {
            path: "notifiers",
            element: (
              <RequirePermission permission="notifiers.view">
                <NotifiersPage />
              </RequirePermission>
            ),
          },
          {
            path: "notifiers/:id",
            element: (
              <RequirePermission permission="notifiers.view">
                <NotifierPage />
              </RequirePermission>
            ),
          },
          {
            path: "retry-policies",
            element: (
              <RequirePermission permission="retry-policies.view">
                <RetryPoliciesPage />
              </RequirePermission>
            ),
          },
          {
            path: "retry-policies/:id",
            element: (
              <RequirePermission permission="retry-policies.view">
                <RetryPolicyPage />
              </RequirePermission>
            ),
          },
          {
            path: "work-groups",
            element: (
              <RequirePermission permission="workgroups.view">
                <WorkGroupsPage />
              </RequirePermission>
            ),
          },
          {
            path: "work-groups/:id",
            element: (
              <RequirePermission permission="workgroups.view">
                <WorkGroupPage />
              </RequirePermission>
            ),
          },
          {
            path: "settings",
            element: (
              <RequirePermission permission="settings.view">
                <SettingsPage />
              </RequirePermission>
            ),
          },
          {
            path: "nodes",
            element: (
              <RequirePermission permission="settings.view">
                <NodesPage />
              </RequirePermission>
            ),
          },
          {
            path: "audit",
            element: (
              <RequirePermission permission="audit.view">
                <AuditPage />
              </RequirePermission>
            ),
          },
          { path: "*", element: <NotFoundPage /> },
        ],
      },
    ],
  },
];

export const router = createBrowserRouter(routes, { basename });
