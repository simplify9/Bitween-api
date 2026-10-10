import { createBrowserRouter, type RouteObject } from "react-router";
import { RequireAuth, RequirePermission } from "./auth/guards";
import { PAGES, type PageId } from "./pages";
import { AppShell } from "./components/layout/AppShell";
import { LoginPage } from "./pages/auth/Login";
import { ChangePasswordPage } from "./pages/auth/ChangePassword";
import { CliLoginPage } from "./pages/auth/CliLogin";
import { NotFoundPage } from "./pages/NotFoundPage";
import { RouteError } from "./pages/RouteError";
import { Suspense, type ReactNode } from "react";
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

/**
 * Which component each page in the registry (src/pages.ts) renders. A `Record` over every page id:
 * a page declared there and not given a component here doesn't compile.
 */
const PAGE_ELEMENTS: Record<PageId, ReactNode> = {
  dashboard: <DashboardPage />,
  exchanges: <ExchangesPage />,
  exchangeNew: <ExchangeNewPage />,
  scheduledRetries: <ScheduledRetriesPage />,
  queueHealth: <QueueHealthPage />,
  flow: <FlowPage />,
  apiGateways: <ApiGatewaysPage />,
  apiGatewayNew: <ApiGatewayNewPage />,
  apiGateway: <ApiGatewayPage />,
  attachPartner: <AttachPartnerPage />,
  gatewaySubscriptionNew: <NewGatewaySubscriptionPage />,
  gatewayAttachment: <EditAttachmentPage />,
  busGateways: <BusGatewaysPage />,
  busGatewayNew: <BusGatewayNewPage />,
  busGateway: <BusGatewayPage />,
  scheduledJobs: <ScheduledJobsPage />,
  scheduledJobNew: <NewScheduledJobPage />,
  scheduledJob: <SubscriptionPage />,
  aggregations: <AggregationsPage />,
  aggregationNew: <NewAggregationPage />,
  aggregation: <SubscriptionPage />,
  responseSubscriptions: <ResponseSubscriptionsPage />,
  responseSubscriptionNew: <NewResponseSubscriptionPage />,
  responseSubscription: <SubscriptionPage />,
  subscriptions: <SubscriptionsPage />,
  subscription: <SubscriptionPage />,
  subscriptionMapper: <MapperEditorRoute />,
  partners: <PartnersPage />,
  partner: <PartnerPage />,
  dataSources: <DataSourcesPage />,
  dataSourceNew: <DataSourceNewPage />,
  dataSource: <DataSourcePage />,
  informationTypes: <InformationTypesPage />,
  informationType: <InformationTypePage />,
  adapters: <AdaptersPage />,
  adapterDraft: (
    <Suspense fallback={<LoadingBlock label="Opening the editor…" />}>
      <AdapterEditorPage />
    </Suspense>
  ),
  globalValues: <GlobalValueSetsPage />,
  globalValueSet: <GlobalValueSetPage />,
  workGroups: <WorkGroupsPage />,
  workGroup: <WorkGroupPage />,
  retryPolicies: <RetryPoliciesPage />,
  retryPolicy: <RetryPolicyPage />,
  notifiers: <NotifiersPage />,
  notifier: <NotifierPage />,
  members: <MembersPage />,
  member: <MembersPage />,
  roles: <RolesPage />,
  roleNew: <RoleEditor />,
  role: <RoleEditor />,
  settings: <SettingsPage />,
  nodes: <NodesPage />,
  audit: <AuditPage />,
  profile: <ProfilePage />,
};

/** What a route carries for the shell: which registry page is showing. */
export interface PageHandle {
  page: PageId;
}

const pageRoutes: RouteObject[] = (Object.keys(PAGES) as PageId[]).map((id) => {
  const { path, permission } = PAGES[id];
  const element = PAGE_ELEMENTS[id];
  return {
    path,
    handle: { page: id } satisfies PageHandle,
    element: permission ? <RequirePermission permission={permission}>{element}</RequirePermission> : element,
  };
});

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
          // Members and Roles are two sidebar entries, not tabs inside a "Team" page, so "/team"
          // itself is no longer a page — only a bookmark people may still hold.
          { path: "team", element: <TeamRedirect /> },
          ...pageRoutes,
          { path: "*", element: <NotFoundPage /> },
        ],
      },
    ],
  },
];

export const router = createBrowserRouter(routes, { basename });
