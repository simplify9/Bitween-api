import { createBrowserRouter, type RouteObject } from "react-router";
import { RequireAuth, RequirePermission } from "./auth/guards";
import { AppShell } from "./components/layout/AppShell";
import { LoginPage } from "./pages/auth/Login";
import { ChangePasswordPage } from "./pages/auth/ChangePassword";
import { CliLoginPage } from "./pages/auth/CliLogin";
import { NotFoundPage } from "./pages/NotFoundPage";
import { RouteError } from "./pages/RouteError";
import { Suspense } from "react";
import {
  AdapterEditorPage,
  AdaptersPage,
  AggregationsPage,
  ApiGatewayNewPage,
  ApiGatewayPage,
  ApiGatewaysPage,
  AttachPartnerPage,
  AuditPage,
  BusGatewayNewPage,
  BusGatewayPage,
  BusGatewaysPage,
  DashboardPage,
  DataSourceNewPage,
  DataSourcePage,
  DataSourcesPage,
  EditAttachmentPage,
  ExchangeNewPage,
  ExchangesPage,
  FlowPage,
  GlobalValueSetPage,
  GlobalValueSetsPage,
  HomeRedirect,
  InformationTypePage,
  InformationTypesPage,
  MapperEditorRoute,
  MembersPage,
  NewAggregationPage,
  NewGatewaySubscriptionPage,
  NewResponseSubscriptionPage,
  NewScheduledJobPage,
  NodesPage,
  NotifierPage,
  NotifiersPage,
  PartnerPage,
  PartnersPage,
  ProfilePage,
  QueueHealthPage,
  ResponseSubscriptionsPage,
  RetryPoliciesPage,
  RetryPolicyPage,
  RoleEditor,
  RolesPage,
  ScheduledJobsPage,
  ScheduledRetriesPage,
  SettingsPage,
  SubscriptionPage,
  SubscriptionsPage,
  TeamRedirect,
  WorkGroupPage,
  WorkGroupsPage,
} from "./routePages";

import { LoadingBlock } from "./components/ui/basics";

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
