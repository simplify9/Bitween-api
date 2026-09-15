import { Navigate, createBrowserRouter } from "react-router";
import { RequireAuth, RequirePermission } from "./auth/guards";
import { useSession } from "./auth/SessionContext";
import { AppShell } from "./components/layout/AppShell";
import { NAV_GROUPS, homePath } from "./nav";
import { LoginPage } from "./pages/auth/Login";
import { ChangePasswordPage } from "./pages/auth/ChangePassword";
import { NotFoundPage, PlaceholderPage } from "./pages/PlaceholderPage";
import { ProfilePage } from "./pages/ProfilePage";
import { AuditPage } from "./pages/audit/AuditPage";
import { SettingsPage } from "./pages/settings/SettingsPage";
import MapperEditorRoute from "./components/nativeMapper/MapperEditorRoute";
import { DashboardPage } from "./pages/dashboard/DashboardPage";
import { ExchangeNewPage } from "./pages/exchanges/ExchangeNewPage";
import { ExchangesPage } from "./pages/exchanges/ExchangesPage";
import { QueueHealthPage } from "./pages/queue-health/QueueHealthPage";
import { ScheduledRetriesPage } from "./pages/scheduled-retries/ScheduledRetriesPage";
import { ApiGatewayNewPage } from "./pages/api-gateways/ApiGatewayNewPage";
import { ApiGatewaysPage } from "./pages/api-gateways/ApiGatewaysPage";
import { ApiGatewayPage } from "./pages/api-gateways/ApiGatewayPage";
import { AttachPartnerPage } from "./pages/api-gateways/AttachPartnerPage";
import { NewGatewaySubscriptionPage } from "./pages/api-gateways/NewGatewaySubscriptionPage";
import { EditAttachmentPage } from "./pages/api-gateways/EditAttachmentPage";
import { BusGatewayNewPage } from "./pages/bus-gateways/BusGatewayNewPage";
import { BusGatewayPage } from "./pages/bus-gateways/BusGatewayPage";
import { BusGatewaysPage } from "./pages/bus-gateways/BusGatewaysPage";
import { DataSourceNewPage } from "./pages/data-sources/DataSourceNewPage";
import { DataSourcePage } from "./pages/data-sources/DataSourcePage";
import { DataSourcesPage } from "./pages/data-sources/DataSourcesPage";
import { FlowPage } from "./pages/flow/FlowPage";
import { GlobalValueSetPage } from "./pages/global-values/GlobalValueSetPage";
import { GlobalValueSetsPage } from "./pages/global-values/GlobalValueSetsPage";
import { InformationTypePage } from "./pages/information-types/InformationTypePage";
import { InformationTypesPage } from "./pages/information-types/InformationTypesPage";
import { SubscriptionPage } from "./pages/subscriptions/SubscriptionPage";
import { SubscriptionsPage } from "./pages/subscriptions/SubscriptionsPage";
import { NotifierPage } from "./pages/notifiers/NotifierPage";
import { NotifiersPage } from "./pages/notifiers/NotifiersPage";
import { PartnerPage } from "./pages/partners/PartnerPage";
import { PartnersPage } from "./pages/partners/PartnersPage";
import { NewScheduledJobPage } from "./pages/scheduled-jobs/NewScheduledJobPage";
import { ScheduledJobsPage } from "./pages/scheduled-jobs/ScheduledJobsPage";
import { AggregationsPage } from "./pages/aggregations/AggregationsPage";
import { NewAggregationPage } from "./pages/aggregations/NewAggregationPage";
import { RetryPoliciesPage } from "./pages/retry-policies/RetryPoliciesPage";
import { RetryPolicyPage } from "./pages/retry-policies/RetryPolicyPage";
import { MembersPage } from "./pages/team/MembersPage";
import { RoleEditor } from "./pages/team/RoleEditor";
import { RolesPage } from "./pages/team/RolesPage";
import { WorkGroupPage } from "./pages/work-groups/WorkGroupPage";
import { WorkGroupsPage } from "./pages/work-groups/WorkGroupsPage";

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

const placeholderRoutes = NAV_GROUPS.flatMap((group) => group.items)
  .filter((item) => item.planned)
  .map((item) => ({
    path: item.path.slice(1),
    element: <PlaceholderPage item={item} />,
  }));

/** "/" → undefined (no basename); "/prefix/" → "/prefix" if ever remounted. */
const basename = import.meta.env.BASE_URL.replace(/\/+$/, "") || undefined;

export const router = createBrowserRouter([
  { path: "/login", element: <LoginPage /> },
  {
    element: <RequireAuth />,
    children: [
      // Inside the auth guard because it needs a session, outside the shell because the session
      // it serves grants nothing — a sidebar built from those permissions would be empty.
      { path: "change-password", element: <ChangePasswordPage /> },
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
            path: "audit",
            element: (
              <RequirePermission permission="audit.view">
                <AuditPage />
              </RequirePermission>
            ),
          },
          ...placeholderRoutes,
          { path: "*", element: <NotFoundPage /> },
        ],
      },
    ],
  },
], { basename });
