import {
  Activity,
  ArrowLeftRight,
  BellRing,
  Cable,
  CalendarClock,
  CornerDownLeft,
  Database,
  FileStack,
  FileText,
  LayoutDashboard,
  Handshake,
  Layers,
  Network,
  Puzzle,
  RefreshCw,
  RotateCcw,
  ScrollText,
  Server,
  Settings,
  ShieldCheck,
  SlidersHorizontal,
  Users,
  Webhook,
  Workflow,
  type LucideIcon,
} from "lucide-react";
import type { PermissionKey } from "./api";

/**
 * Every page of the app, declared once.
 *
 * The router, the sidebar, the breadcrumbs, the tab title, the role editor's access preview and
 * the command palette all read this. A page used to be described in three places — its route and
 * guard in the router, its sidebar entry in nav.ts with the same permission again, and its
 * full-bleed layout in a list in the shell — and nothing kept them in step.
 *
 * No components here, so anything can import it without pulling in a page: the router pairs each
 * id with its component (src/router.tsx), and a page without one doesn't compile.
 *
 * Adding a page:
 * 1. Declare it below — `nav` for a sidebar entry, `parent` for where its breadcrumb leads.
 * 2. Give it its component in `PAGE_ELEMENTS` (src/router.tsx); lazy-load it in routePages.tsx.
 * 3. A page about one thing names it: `usePageTitle(thing.name)`.
 * 4. A list page builds on useListParams (lib/listParams), SearchBox and ListBody — see
 *    RetryPoliciesPage for the whole of one.
 */

export type NavGroupId = "operate" | "subscriptions" | "configuration" | "administration";

export interface PageDef<Id extends string = string> {
  /** "subscriptions/:id" — relative to the app root, as the router takes it. */
  path: string;
  /** What the page is called: its tab title, its breadcrumb, its sidebar label. */
  title: string;
  /** Needed to open it. None: any signed-in session. */
  permission?: PermissionKey;
  /** The page this one sits under, for breadcrumbs. */
  parent?: Id;
  /** In the sidebar, under this group. Order within the group is the order here. */
  nav?: { group: NavGroupId; icon: LucideIcon };
  /** Fills the viewport instead of sitting in the shell's page frame (own scrolling regions). */
  layout?: "full";
  /** Found by the command palette under these words too. */
  keywords?: string[];
}

const definePages = <Id extends string>(pages: Record<Id, PageDef<NoInfer<Id>>>) => pages;

export const PAGES = definePages({
  // First in the sidebar: where you land, and the way back to "how is everything doing".
  dashboard: {
    path: "dashboard",
    title: "Dashboard",
    permission: "dashboard.view",
    nav: { group: "operate", icon: LayoutDashboard },
    keywords: ["home", "overview"],
  },

  // ── Operate ──
  exchanges: {
    path: "exchanges",
    title: "Exchanges",
    permission: "exchanges.view",
    nav: { group: "operate", icon: ArrowLeftRight },
    keywords: ["documents", "messages", "traffic"],
  },
  exchangeNew: { path: "exchanges/new", title: "New exchange", permission: "exchanges.operate", parent: "exchanges" },
  scheduledRetries: {
    path: "scheduled-retries",
    title: "Scheduled retries",
    permission: "exchanges.view",
    nav: { group: "operate", icon: RefreshCw },
  },
  queueHealth: {
    path: "queue-health",
    title: "Queue health",
    permission: "monitoring.view",
    nav: { group: "operate", icon: Activity },
    keywords: ["rabbitmq", "dead letters", "consumers"],
  },
  // Read-only like the two above: the picture of how gateways join up, not a kind of gateway.
  // Gated on the bus alone: bus messages are what carry work *between* gateways, so without that
  // permission there is no flow to map.
  flow: {
    path: "flow",
    title: "Flow map",
    permission: "bus-gateways.view",
    nav: { group: "operate", icon: Network },
    layout: "full",
    keywords: ["diagram", "topology"],
  },

  // ── Subscriptions ──
  // Entry points — how a document gets in — then the pipelines it runs through, then the whole
  // list, then who it's with. The heading names the area, not the entity: a gateway is not itself
  // a subscription.
  apiGateways: {
    path: "api-gateways",
    title: "API gateways",
    permission: "api-gateways.view",
    nav: { group: "subscriptions", icon: Webhook },
    keywords: ["http", "endpoint", "inbound"],
  },
  apiGatewayNew: {
    path: "api-gateways/new",
    title: "New API gateway",
    permission: "api-gateways.create",
    parent: "apiGateways",
  },
  apiGateway: {
    path: "api-gateways/:id",
    title: "API gateway",
    permission: "api-gateways.view",
    parent: "apiGateways",
  },
  attachPartner: {
    path: "api-gateways/:id/attach",
    title: "Attach a partner",
    permission: "api-gateways.edit",
    parent: "apiGateway",
  },
  gatewaySubscriptionNew: {
    path: "api-gateways/:id/attach/new-subscription",
    title: "New subscription",
    permission: "subscriptions.create",
    parent: "attachPartner",
  },
  gatewayAttachment: {
    path: "api-gateways/:id/attachments/:partnerId",
    title: "Attachment",
    permission: "api-gateways.edit",
    parent: "apiGateway",
  },
  busGateways: {
    path: "bus-gateways",
    title: "Bus gateways",
    permission: "bus-gateways.view",
    nav: { group: "subscriptions", icon: Cable },
    keywords: ["routes", "messages"],
  },
  busGatewayNew: {
    path: "bus-gateways/new",
    title: "New bus gateway",
    permission: "bus-gateways.create",
    parent: "busGateways",
  },
  busGateway: {
    path: "bus-gateways/:id",
    title: "Bus gateway",
    permission: "bus-gateways.view",
    parent: "busGateways",
    layout: "full",
  },
  scheduledJobs: {
    path: "scheduled-jobs",
    title: "Scheduled jobs",
    permission: "subscriptions.view",
    nav: { group: "subscriptions", icon: CalendarClock },
    keywords: ["receiver", "cron", "schedule"],
  },
  scheduledJobNew: {
    path: "scheduled-jobs/new",
    title: "New scheduled job",
    permission: "subscriptions.create",
    parent: "scheduledJobs",
  },
  scheduledJob: {
    path: "scheduled-jobs/:id",
    title: "Scheduled job",
    permission: "subscriptions.view",
    parent: "scheduledJobs",
  },
  // Directly under scheduled jobs: it is the other thing that runs on a schedule, and it collects
  // what one of these produced.
  aggregations: {
    path: "aggregations",
    title: "Aggregations",
    permission: "subscriptions.view",
    nav: { group: "subscriptions", icon: FileStack },
  },
  aggregationNew: {
    path: "aggregations/new",
    title: "New aggregation",
    permission: "subscriptions.create",
    parent: "aggregations",
  },
  aggregation: {
    path: "aggregations/:id",
    title: "Aggregation",
    permission: "subscriptions.view",
    parent: "aggregations",
  },
  // Last of the pipelines, because it runs on what one of the others delivered.
  responseSubscriptions: {
    path: "response-subscriptions",
    title: "Response subscriptions",
    permission: "subscriptions.view",
    nav: { group: "subscriptions", icon: CornerDownLeft },
  },
  responseSubscriptionNew: {
    path: "response-subscriptions/new",
    title: "New response subscription",
    permission: "subscriptions.create",
    parent: "responseSubscriptions",
  },
  responseSubscription: {
    path: "response-subscriptions/:id",
    title: "Response subscription",
    permission: "subscriptions.view",
    parent: "responseSubscriptions",
  },
  subscriptions: {
    path: "subscriptions",
    title: "All subscriptions",
    permission: "subscriptions.view",
    nav: { group: "subscriptions", icon: Workflow },
    keywords: ["pipelines"],
  },
  subscription: {
    path: "subscriptions/:id",
    title: "Subscription",
    permission: "subscriptions.view",
    parent: "subscriptions",
  },
  subscriptionMapper: {
    path: "subscriptions/:id/mapper",
    title: "Mapping editor",
    permission: "subscriptions.edit",
    parent: "subscription",
  },
  partners: {
    path: "partners",
    title: "Partners",
    permission: "partners.view",
    nav: { group: "subscriptions", icon: Handshake },
    keywords: ["customers", "suppliers", "api keys"],
  },
  partner: { path: "partners/:id", title: "Partner", permission: "partners.view", parent: "partners" },

  // ── Configuration ──
  // First in Configuration: a data source is a connection to something outside Bitween — a broker
  // feeding a gateway, or a database a subscription runs statements against.
  dataSources: {
    path: "data-sources",
    title: "Data sources",
    permission: "data-sources.view",
    nav: { group: "configuration", icon: Database },
    keywords: ["database", "broker", "connection"],
  },
  dataSourceNew: {
    path: "data-sources/new",
    title: "New data source",
    permission: "data-sources.create",
    parent: "dataSources",
  },
  dataSource: {
    path: "data-sources/:id",
    title: "Data source",
    permission: "data-sources.view",
    parent: "dataSources",
  },
  informationTypes: {
    path: "information-types",
    title: "Information types",
    permission: "documents.view",
    nav: { group: "configuration", icon: FileText },
    keywords: ["document types", "schema"],
  },
  informationType: {
    path: "information-types/:id",
    title: "Information type",
    permission: "documents.view",
    parent: "informationTypes",
  },
  // What subscriptions are built from — receivers, validators, mappers and handlers, built in or
  // published — with their versions and who uses them.
  adapters: {
    path: "adapters",
    title: "Adapters",
    permission: "subscriptions.view",
    nav: { group: "configuration", icon: Puzzle },
    keywords: ["handlers", "mappers", "receivers", "plugins", "marketplace", "installed"],
  },
  adapterDraft: {
    path: "adapters/drafts/:id",
    title: "Adapter draft",
    permission: "adapter-source.edit",
    parent: "adapters",
  },
  globalValues: {
    path: "global-values",
    title: "Global values",
    permission: "global-values.view",
    nav: { group: "configuration", icon: SlidersHorizontal },
    keywords: ["value sets", "variables"],
  },
  globalValueSet: {
    path: "global-values/:id",
    title: "Value set",
    permission: "global-values.view",
    parent: "globalValues",
  },
  workGroups: {
    path: "work-groups",
    title: "Work groups",
    permission: "workgroups.view",
    nav: { group: "configuration", icon: Layers },
    keywords: ["queues", "lanes"],
  },
  workGroup: { path: "work-groups/:id", title: "Work group", permission: "workgroups.view", parent: "workGroups" },
  retryPolicies: {
    path: "retry-policies",
    title: "Retry policies",
    permission: "retry-policies.view",
    nav: { group: "configuration", icon: RotateCcw },
  },
  retryPolicy: {
    path: "retry-policies/:id",
    title: "Retry policy",
    permission: "retry-policies.view",
    parent: "retryPolicies",
  },
  // Directly under retry policies: both are about what happens when something goes wrong, and a
  // budget-exhausted alert is delivered by a notifier.
  notifiers: {
    path: "notifiers",
    title: "Notifiers",
    permission: "notifiers.view",
    nav: { group: "configuration", icon: BellRing },
    keywords: ["alerts", "email"],
  },
  notifier: { path: "notifiers/:id", title: "Notifier", permission: "notifiers.view", parent: "notifiers" },

  // ── Administration ──
  // Who can sign in, then what signing in lets them do. Two entries rather than one "Team" with
  // tabs: they are gated on different permissions.
  members: {
    path: "team/members",
    title: "Members",
    permission: "users.view",
    nav: { group: "administration", icon: Users },
    keywords: ["users", "people", "accounts"],
  },
  member: { path: "team/members/:id", title: "Member", permission: "users.view", parent: "members" },
  roles: {
    path: "team/roles",
    title: "Roles",
    permission: "roles.view",
    nav: { group: "administration", icon: ShieldCheck },
    keywords: ["permissions", "access"],
  },
  roleNew: { path: "team/roles/new", title: "New role", permission: "roles.create", parent: "roles" },
  role: { path: "team/roles/:id", title: "Role", permission: "roles.view", parent: "roles" },
  settings: {
    path: "settings",
    title: "Settings",
    permission: "settings.view",
    nav: { group: "administration", icon: Settings },
    keywords: ["configuration", "branding", "sso"],
  },
  nodes: {
    path: "nodes",
    title: "Nodes",
    permission: "settings.view",
    nav: { group: "administration", icon: Server },
    keywords: ["cluster", "servers"],
  },
  // Last: it reports on everything above it rather than configuring anything, and it is the one
  // page whose value is that nobody can quietly change it.
  audit: {
    path: "audit",
    title: "Audit trail",
    permission: "audit.view",
    nav: { group: "administration", icon: ScrollText },
    keywords: ["history", "changes", "log"],
  },

  profile: { path: "profile", title: "Your profile", keywords: ["password", "account"] },
});

export type PageId = keyof typeof PAGES;

export const pageById = (id: PageId): PageDef<PageId> => PAGES[id];

/** The pages above `id`, outermost first, ending with `id` itself. */
export function pageTrail(id: PageId): PageId[] {
  const trail: PageId[] = [];
  for (let at: PageId | undefined = id; at; at = PAGES[at].parent) trail.unshift(at);
  return trail;
}

/** "/subscriptions/:id" with `{ id: "5" }` → "/subscriptions/5". Null while a parameter is missing. */
export function pathOf(id: PageId, params: Record<string, string | undefined> = {}): string | null {
  let missing = false;
  const filled = PAGES[id].path.replace(/:(\w+)/g, (_, name: string) => {
    const value = params[name];
    if (value === undefined) missing = true;
    return encodeURIComponent(value ?? "");
  });
  return missing ? null : `/${filled}`;
}
