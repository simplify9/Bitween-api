import { Navigate, createBrowserRouter } from "react-router";
import { LayoutDashboard } from "lucide-react";
import { RequireAuth, RequirePermission } from "./auth/guards";
import { useSession } from "./auth/SessionContext";
import { AppShell } from "./components/layout/AppShell";
import { NAV_GROUPS, homePath, type NavItem } from "./nav";
import { AcceptInvitePage } from "./pages/auth/AcceptInvite";
import { ForgotPasswordPage } from "./pages/auth/ForgotPassword";
import { LoginPage } from "./pages/auth/Login";
import { ResetPasswordPage } from "./pages/auth/ResetPassword";
import { NotFoundPage, PlaceholderPage } from "./pages/PlaceholderPage";
import { ProfilePage } from "./pages/ProfilePage";
import { SettingsPage } from "./pages/settings/SettingsPage";
import MappingEditor from "./components/mapper/MappingEditor";
import { ApiGatewayNewPage } from "./pages/api-gateways/ApiGatewayNewPage";
import { ApiGatewayPage } from "./pages/api-gateways/ApiGatewayPage";
import { AttachPartnerWizard } from "./pages/api-gateways/AttachPartnerWizard";
import { EditAttachmentPage } from "./pages/api-gateways/EditAttachmentPage";
import { AddRouteWizard } from "./pages/bus-gateways/AddRouteWizard";
import { BusGatewayNewPage } from "./pages/bus-gateways/BusGatewayNewPage";
import { BusGatewayPage } from "./pages/bus-gateways/BusGatewayPage";
import { EditRoutePage } from "./pages/bus-gateways/EditRoutePage";
import { GlobalValueSetPage } from "./pages/global-values/GlobalValueSetPage";
import { GlobalValueSetsPage } from "./pages/global-values/GlobalValueSetsPage";
import { InformationTypeNewPage } from "./pages/information-types/InformationTypeNewPage";
import { InformationTypePage } from "./pages/information-types/InformationTypePage";
import { InformationTypesPage } from "./pages/information-types/InformationTypesPage";
import { IntegrationNewPage } from "./pages/integrations/IntegrationNewPage";
import { IntegrationPage } from "./pages/integrations/IntegrationPage";
import { IntegrationsPage } from "./pages/integrations/IntegrationsPage";
import { NotifierPage } from "./pages/notifiers/NotifierPage";
import { NotifiersPage } from "./pages/notifiers/NotifiersPage";
import { PartnerNewPage } from "./pages/partners/PartnerNewPage";
import { PartnerPage } from "./pages/partners/PartnerPage";
import { PartnersPage } from "./pages/partners/PartnersPage";
import { NewScheduledJobWizard } from "./pages/scheduled-jobs/NewScheduledJobWizard";
import { RetryPoliciesPage } from "./pages/retry-policies/RetryPoliciesPage";
import { RetryPolicyPage } from "./pages/retry-policies/RetryPolicyPage";
import { MembersTab } from "./pages/team/MembersTab";
import { RoleEditor } from "./pages/team/RoleEditor";
import { RolesTab } from "./pages/team/RolesTab";
import { TeamIndexRedirect, TeamPage } from "./pages/team/TeamPage";
import { WorkGroupNewPage } from "./pages/work-groups/WorkGroupNewPage";
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

/**
 * Dashboard has no sidebar entry (the logo links here instead) so it isn't
 * derived from NAV_GROUPS like the other placeholders — it still needs its
 * own route.
 */
const dashboardNavItem: NavItem = {
  label: "Dashboard",
  path: "/dashboard",
  icon: LayoutDashboard,
  permissions: ["dashboard.view"],
  planned: true,
};

/** "/bitween/" → "/bitween"; "/" → undefined (no basename). */
const basename = import.meta.env.BASE_URL.replace(/\/+$/, "") || undefined;

export const router = createBrowserRouter([
  { path: "/login", element: <LoginPage /> },
  { path: "/forgot-password", element: <ForgotPasswordPage /> },
  { path: "/reset-password/:token", element: <ResetPasswordPage /> },
  { path: "/invite/:token", element: <AcceptInvitePage /> },
  {
    element: <RequireAuth />,
    children: [
      {
        element: <AppShell />,
        children: [
          { index: true, element: <HomeRedirect /> },
          { path: "dashboard", element: <PlaceholderPage item={dashboardNavItem} /> },
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
            path: "subscriptions/new",
            element: (
              <RequirePermission permission="subscriptions.create">
                <IntegrationNewPage />
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
          // the gateway/job list pages live inside /subscriptions now
          { path: "api-gateways", element: <Navigate to="/subscriptions?types=api-gateways" replace /> },
          { path: "bus-gateways", element: <Navigate to="/subscriptions?types=bus-gateways" replace /> },
          { path: "scheduled-jobs", element: <Navigate to="/subscriptions?types=scheduled-jobs" replace /> },
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
                <AttachPartnerWizard />
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
            path: "bus-gateways/:id/add-route",
            element: (
              <RequirePermission permission="bus-gateways.edit">
                <AddRouteWizard />
              </RequirePermission>
            ),
          },
          {
            path: "bus-gateways/:id/routes/:routeId",
            element: (
              <RequirePermission permission="bus-gateways.edit">
                <EditRoutePage />
              </RequirePermission>
            ),
          },
          {
            path: "scheduled-jobs/new",
            element: (
              <RequirePermission permission="subscriptions.create">
                <NewScheduledJobWizard />
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
            path: "partners/new",
            element: (
              <RequirePermission permission="partners.create">
                <PartnerNewPage />
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
            path: "information-types/new",
            element: (
              <RequirePermission permission="documents.create">
                <InformationTypeNewPage />
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
            path: "work-groups/new",
            element: (
              <RequirePermission permission="workgroups.create">
                <WorkGroupNewPage />
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
