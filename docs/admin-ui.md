# Admin UI

The admin UI is a React single-page app in `SW.Bitween.Web/ClientApp`. It is built into `SW.Bitween.Web/wwwroot` and served by the same process as the API, from the site root.

Every page and action is gated by permissions. Anything a member cannot use is hidden, not greyed out. Filters, tabs, selected stages and open dialogs are kept in the URL, so any view can be shared as a link.

## Signing in

The sign-in page offers email and password, Microsoft, an [OpenID Connect provider](security.md#openid-connect), or any of them together, depending on settings. With a mail server it also offers **Forgot password?**, and invite and reset emails lead to a page where the member chooses their password. After sign-in, members land on the first page their permissions allow. Sessions end after 30 minutes with no activity.

## Operate

| Page | What it does |
|---|---|
| **Exchanges** | Search and filter every exchange with live refresh, optionally showing only the latest attempt of each retry chain. A filter's operator is picked on its chip: *is* or *is not*, *contains* or *does not contain*; subscription, partner and information type can pick several (*is any of*, *is none of*), and any number of promoted properties can be filtered on at once. Expand a row to see each stage's file, formatted or raw, plus the exception, promoted properties and the retry chain. On a failed exchange, **Explain this failure** asks the AI provider for what went wrong and what to change ([Explaining a failure](configuration.md#explaining-a-failure)). Retry one exchange, a selection, or every match of the current filters after previewing the plan. Create an exchange by hand. **Ask** turns a plain-English question into these filters with AI ([Asking your exchanges](configuration.md#asking-your-exchanges)). |
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
| **Adapters** | Built-in and custom adapters with their settings, versions and the subscriptions that use them. With `adapter-source.view`, read the source a version was published with and compare two versions. With `adapter-source.edit`, write Python, JavaScript and TypeScript adapters in the editor; with `adapter-source.operate`, publish them and make a version current. The **Marketplace** tab lists the connectors of the registry set under Settings, marks those already installed or with an update, and installs one ([Installing from the marketplace](adapters.md#installing-from-the-marketplace)). |

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

With no stage selected, the overview shows health, next and last run, work group, retry policy, a retry budget banner once a budget is spent, receive attempts or recent runs, recent exchanges, notifications and change history. The **Notifications** card picks channels and when each sends; a new channel can be made from there without losing the page's edits. Header buttons pause or resume, receive or roll up now, create an aggregation of this subscription, and delete it.

Edits stay a draft until saved from the bar at the bottom of the page.

#### Versions

Each save that changes a subscription's configuration, from creating it on, is recorded as a version:
who saved it, when, and which settings it changed. The **Versions** panel on the overview lists the last
50. **Compare** shows each setting that differs from now, side by side. **Restore** saves the version's
configuration as the subscription: checked like any save, recorded as a new version, and leaving whether
it is active as it is.

A version never holds a secret. A setting its adapter marks as secret, or one the adapter doesn't
describe, is kept as `__private__`, so restoring keeps the value the subscription has now, unless the
version points at another destination (a host, a URL): a save that changes a destination drops the
secrets the sentinel stood for, so they are entered again. Running state, such as failure counts and
pauses, is not part of a version. Subscriptions saved before versions existed start their history at
their next save.

## Configuration

| Page | What it does |
|---|---|
| **Data sources** | Broker and database connections. Settings forms are built from the adapter. Test a connection, watch live health, and for databases manage SQL statements with save-time checks and browse the schema. |
| **Information types** | Name, code, format, promoted properties, bus availability and message type name, and what uses each type. Promoted properties are dragged into order, which is the order lists show them in; the first names archived exchanges. With a duplicate window, the type's page counts the week's [duplicates](concepts.md#duplicates) and offers **Drop duplicates**. |
| **Global values** | Value sets, and the subscriptions that reference each key. |
| **Work groups** | Queue settings and live throughput per lane. |
| **Retry policies** | Groups, conditions, budgets and which notification channel each alert goes through. Each group opens on a page of its own and saves the policy from there. The usage panel shows spent budgets per subscription, and the test panel runs the draft policy against sample errors. |

## Administration

| Page | What it does |
|---|---|
| **Team** | Members: add with an invite (emailed when a mail server is set, otherwise a link to send) or a password, resend an expired invite, assign roles, set a password, unlock, disable and remove. Invited members show as *Invited* until they choose a password. Roles: a permission matrix with a live preview of the navigation the role would see. |
| **Settings** | Runtime settings by section, including branding with an instant preview. Values owned by the environment are shown read-only. Documents & storage shows the bucket's deletion rules and what the retention settings do, and asks before saving a change to them. **Mail server** is the server Email channels send through. **AI** is the provider and API key AI features use ([AI](configuration.md#ai)). **Notification channels** lists channels and who uses each, and links to any legacy notifiers. Each channel opens on a page of its own: Email, Microsoft Teams and Slack first, any other adapter one click away, a test message, and its recent sends. A role with the notification permissions but not Settings, such as the built-in Member and Viewer, finds only the Notification channels section here. |
| **Audit trail** | Every configuration change with its old and new values. |

On a staging that mirrors prod, the subscription and gateway lists can bring prod's items in and say how each one's test against prod went, and each subscription's page compares it with prod's and tests it with prod's traffic. See [Prod mirror](prod-mirror.md).

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
