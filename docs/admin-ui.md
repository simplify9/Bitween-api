# Admin UI

The admin UI is a React single-page app in `SW.Bitween.Web/ClientApp`. It is built into `SW.Bitween.Web/wwwroot` and served by the same process as the API, from the site root.

Every page and action is gated by permissions. Anything a member cannot use is hidden, not greyed out. Filters, tabs, selected stages and open dialogs are kept in the URL, so any view can be shared as a link.

## Signing in

The sign-in page offers email and password, Microsoft, or both, depending on settings. After sign-in, members land on the first page their permissions allow. Sessions end after 30 minutes with no activity.

## Operate

| Page | What it does |
|---|---|
| **Exchanges** | Search and filter every exchange with live refresh, optionally showing only the latest attempt of each retry chain. Expand a row to see each stage's file, formatted or raw, plus the exception, promoted properties and the retry chain. Retry one exchange, a selection, or every match of the current filters after previewing the plan. Create an exchange by hand. |
| **Scheduled retries** | Automatic retries waiting to run, with the policy that scheduled each one. Run one now. |
| **Queue health** | Live RabbitMQ lanes, backlogs, dead letters, alerts and orphaned queues. |

## Subscriptions

| Page | What it does |
|---|---|
| **API gateways** | Create gateways, copy their sync and async URLs, attach partners with their subscriptions, deactivate or delete. |
| **Bus gateways** | A canvas per gateway showing each route, its subscription's stages and where the response lands on the bus. Edit routes, filters, partners and subscriptions in a side inspector and save them together. Choose whether the gateway reads the internal bus or a queue on a customer's broker. |
| **Scheduled jobs** | Receiving subscriptions with last run, reliability, next run and schedule health. Create a job with its source, schedule, transformation, delivery and response in one form, or receive now. |
| **Aggregations** | Aggregation subscriptions and their sources. Create one, or roll up now. |
| **Flow map** | A read-only map of gateways, bus message types and subscriptions, with warnings for loops, gateways without partners or routes, and messages nobody listens to. |
| **All subscriptions** | Every subscription, filtered by type, information type, partner and status. |
| **Partners** | Partner properties and API keys, and everything that uses each partner. |

### Subscription studio

Opening a subscription shows its pipeline as a row of stages, which depend on its type.

| Type | Stages |
|---|---|
| Scheduled job | Source, Schedule, Transformation, Delivery, Response |
| Aggregation | Rolls up, Schedule, Transformation, Delivery, Response |
| API gateway and API call | Trigger, Validation, Transformation, Delivery, Response |
| Bus gateway | Trigger, Transformation, Delivery, Response |
| Internal | Trigger, Transformation, Delivery, Response |

Selecting a stage opens its editor. Adapter stages pick an adapter and fill in its properties, with a menu for inserting partner and global tokens and a hint showing what each token resolves to. The Transformation stage opens the visual mapping editor.

When a stage uses a database or broker adapter, it also binds the subscription to a data source and picks the statement and operation, the receive mode and batch size, or where to publish. See [Databases](databases.md) and [External brokers](external-brokers.md).

With no stage selected, the overview shows health, next and last run, work group, retry policy, a retry budget banner once a budget is spent, receive attempts or recent runs, recent exchanges, notifications and change history. The **Notifications** card picks channels and the outcomes each sends on; a new channel can be made there without leaving the page. Header buttons pause or resume, receive or roll up now, create an aggregation of this subscription, and delete it.

Edits stay a draft until saved from the bar at the bottom of the page.

## Configuration

| Page | What it does |
|---|---|
| **Data sources** | Broker and database connections. Settings forms are built from the adapter. Test a connection, watch live health, and for databases manage SQL statements with save-time checks and browse the schema. |
| **Information types** | Name, code, format, promoted properties, bus availability and message type name, and what uses each type. |
| **Global values** | Value sets, and the subscriptions that reference each key. |
| **Work groups** | Queue settings and live throughput per lane. |
| **Retry policies** | Groups, conditions, budgets and which notification channel each alert goes through. The usage panel shows spent budgets per subscription, and the test panel runs the draft policy against sample errors. |

## Administration

| Page | What it does |
|---|---|
| **Team** | Members: add, assign roles, set a password, unlock, disable and remove. Roles: a permission matrix with a live preview of the navigation the role would see. |
| **Settings** | Runtime settings by section, including branding with an instant preview. Values owned by the environment are shown read-only. The **Notification channels** section lists, creates and edits channels, shows who uses each, and links to any legacy notifiers. |
| **Audit trail** | Every configuration change with its old and new values. |

The **Dashboard** opens from the logo, and includes failures still to act on and the retry chains that keep failing. **Profile** lets members change their display name and password.

## Mapping editors

`/subscriptions/{id}/mapper` opens the rules-based editor, unless the subscription uses the legacy JSON mapper, which keeps its own editor. See [Mapping](mapping.md).

## Building and testing

```bash
cd SW.Bitween.Web/ClientApp
yarn install
yarn build        # type-checks and writes into ../wwwroot
yarn test         # Vitest
yarn lint         # oxlint
yarn test:e2e     # Playwright, against a running API and database
```

The Vite dev server has no proxy to the API, and the app calls `/api` on its own origin. The working loop is to build, then let the .NET host serve the result. See [Development](development.md).

The UI is built with React 19, TypeScript, Vite, Tailwind CSS 4, React Router, TanStack Query, CodeMirror and MSAL.
