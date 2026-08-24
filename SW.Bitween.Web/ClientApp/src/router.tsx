import { Navigate, createBrowserRouter } from "react-router";
import { RequireAuth, RequirePermission } from "./auth/guards";
import { useSession } from "./auth/SessionContext";
import { AppShell } from "./components/layout/AppShell";
import { NAV_GROUPS, homePath } from "./nav";
import { LoginPage } from "./pages/auth/Login";
import { NotFoundPage, PlaceholderPage } from "./pages/PlaceholderPage";
import { ProfilePage } from "./pages/ProfilePage";
import { SettingsPage } from "./pages/settings/SettingsPage";
import MappingEditor from "./components/mapper/MappingEditor";
import { DashboardPage } from "./pages/dashboard/DashboardPage";
import { ExchangeNewPage } from "./pages/exchanges/ExchangeNewPage";
import { ExchangesPage } from "./pages/exchanges/ExchangesPage";
import { QueueHealthPage } from "./pages/queue-health/QueueHealthPage";
import { ScheduledRetriesPage } from "./pages/scheduled-retries/ScheduledRetriesPage";
import { ApiGatewayNewPage } from "./pages/api-gateways/ApiGatewayNewPage";
import { ApiGatewaysPage } from "./pages/api-gateways/ApiGatewaysPage";
import { ApiGatewayPage } from "./pages/api-gateways/ApiGatewayPage";
import { AttachPartnerPage } from "./pages/api-gateways/AttachPartnerPage";
import { NewGatewayIntegrationPage } from "./pages/api-gateways/NewGatewayIntegrationPage";
import { EditAttachmentPage } from "./pages/api-gateways/EditAttachmentPage";
import { BusGatewayNewPage } from "./pages/bus-gateways/BusGatewayNewPage";
import { BusGatewayPage } from "./pages/bus-gateways/BusGatewayPage";
import { BusGatewaysPage } from "./pages/bus-gateways/BusGatewaysPage";
import { FlowPage } from "./pages/flow/FlowPage";
import { GlobalValueSetPage } from "./pages/global-values/GlobalValueSetPage";
import { GlobalValueSetsPage } from "./pages/global-values/GlobalValueSetsPage";
import { InformationTypePage } from "./pages/information-types/InformationTypePage";
import { InformationTypesPage } from "./pages/information-types/InformationTypesPage";
import { IntegrationPage } from "./pages/integrations/IntegrationPage";
import { IntegrationsPage } from "./pages/integrations/IntegrationsPage";
import { NotifierPage } from "./pages/notifiers/NotifierPage";
import { NotifiersPage } from "./pages/notifiers/NotifiersPage";
import { PartnerPage } from "./pages/partners/PartnerPage";
import { PartnersPage } from "./pages/partners/PartnersPage";
import { NewScheduledJobPage } from "./pages/scheduled-jobs/NewScheduledJobPage";
import { ScheduledJobsPage } from "./pages/scheduled-jobs/ScheduledJobsPage";
import { RetryPoliciesPage } from "./pages/retry-policies/RetryPoliciesPage";
import { RetryPolicyPage } from "./pages/retry-policies/RetryPolicyPage";
import { MembersTab } from "./pages/team/MembersTab";
import { RoleEditor } from "./pages/team/RoleEditor";
import { RolesTab } from "./pages/team/RolesTab";
import { TeamIndexRedirect, TeamPage } from "./pages/team/TeamPage";
import { WorkGroupPage } from "./pages/work-groups/WorkGroupPage";
import { WorkGroupsPage } from "./pages/work-groups/WorkGroupsPage";

/** "/" lands on the first page this session is allowed to see. */
function HomeRedirect() {
  const { session } = useSession();
  if (!session) return null; // RequireAuth already handled this
  return <Navigate to={homePath(session)} replace />;
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
          {
            path: "team",
            element: <TeamPage />,
            children: [
              { index: true, element: <TeamIndexRedirect /> },
              {
                path: "members",
                element: (
                  <RequirePermission permission="users.view">
                    <MembersTab />
                  </RequirePermission>
                ),
              },
              {
                path: "members/:id",
                element: (
                  <RequirePermission permission="users.view">
                    <MembersTab />
                  </RequirePermission>
                ),
              },
              {
                path: "roles",
                element: (
                  <RequirePermission permission="roles.view">
                    <RolesTab />
                  </RequirePermission>
                ),
              },
            ],
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
                <IntegrationsPage />
              </RequirePermission>
            ),
          },
          {
            path: "subscriptions/:id",
            element: (
              <RequirePermission permission="subscriptions.view">
                <IntegrationPage />
              </RequirePermission>
            ),
          },
          {
            path: "subscriptions/:id/mapper",
            element: (
              <RequirePermission permission="subscriptions.edit">
                <MappingEditor />
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
            path: "api-gateways/:id/attach/new-integration",
            element: (
              <RequirePermission permission="subscriptions.create">
                <NewGatewayIntegrationPage />
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
          ...placeholderRoutes,
          { path: "*", element: <NotFoundPage /> },
        ],
      },
    ],
  },
], { basename });
