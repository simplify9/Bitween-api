# AI apps (MCP)

Bitween is an [MCP](https://modelcontextprotocol.io) server at `https://<your-bitween>/mcp`. An AI app such
as Claude or ChatGPT connects to it and works with Bitween as whoever signed it in: it searches exchanges,
reads their files, retries them and pauses subscriptions, within that account's roles.

It is off until an administrator turns on **Settings, MCP**. Turning it off stops every connected app at
once. Files an app reads go to that app and its AI provider, so turning it on is the organization's call.

## Signing an app in

| Way | For | How |
|---|---|---|
| OAuth | People, in claude.ai, Claude Desktop, Claude Code, ChatGPT and other apps that sign in with OAuth | Add the address as a connector. The app opens Bitween, the person signs in as usual and allows it |
| Service-account token | Tools and scripts, in apps that take a request header: Claude Code, Cursor, VS Code | Send `Authorization: Bearer bwt_...`, a [service account](client-sdk.md#a-service-account)'s token |

With OAuth the app acts as the person who allowed it, with their roles; with a token, as the service
account, with its roles. Give either only what the app needs.

Claude Code, with OAuth:

```sh
claude mcp add --transport http bitween https://bitween.example.com/mcp
```

With a service-account token:

```sh
claude mcp add --transport http bitween https://bitween.example.com/mcp --header "Authorization: Bearer bwt_..."
```

### OAuth

- Apps find how to sign in from the endpoint itself: `/mcp` answers 401 with a pointer to
  `/.well-known/oauth-protected-resource/mcp`, which names Bitween as the OAuth server, and
  `/.well-known/oauth-authorization-server` describes it.
- An app says who it is with the address of its own metadata document (Client ID Metadata Document), as
  Claude and ChatGPT do, or registers at `/api/oauth/register`. Neither is stored: a registered client id
  carries the app's name and addresses, signed.
- The app sends the person to the admin UI's **Connect** page (`/oauth/authorize`). It names the app and
  where approving sends them back, and warns when that is a program on their own computer. The person is
  signed in there however they sign in, Microsoft and OpenID Connect included.
- Approving hands the app a one-time code, good for two minutes, once, with the PKCE verifier the app
  kept, and only for that app and address. The app trades it at `/api/oauth/token` for an access token and
  a refresh token.
- The access token works at `/mcp` and nowhere else; an API session doesn't work at `/mcp`. It lasts the
  **Sign-in session length**. The refresh token renews it, for that app only, and ends after 30 days
  unused.
- Each person's connected apps are on their profile, and on each member's page for administrators, with
  **Disconnect**. Disconnecting stops renewals; the access the app holds ends within a session's length.

OAuth addresses are built on `Bitween:PublicUrl`. Set it to the address people use, or apps are given
addresses they can't reach.

## Tools

Each tool needs the permission the same screen needs.

| Tool | What it does | Permission |
|---|---|---|
| `search_exchanges` | Exchanges by status, time, partner, subscription, information type, id, correlation id, error text, promoted properties, retries | `exchanges.view` or `dashboard.view` |
| `summarize_exchanges` | How many match, by status | `exchanges.view` or `dashboard.view` |
| `get_exchange` | One exchange and its retry chain | `exchanges.view` |
| `read_exchange_file` | An exchange's input, output or response, up to 200,000 characters | `exchanges.view` |
| `retry_exchange` | Retries one exchange and answers with the new attempt | `exchanges.operate` |
| `preview_retry_exchanges`, `retry_exchanges` | Retries what filters match, at most 500, at least one filter | `exchanges.operate` |
| `list_scheduled_retries`, `run_scheduled_retry_now` | Automatic retries waiting, and running one now | `exchanges.view`, `exchanges.operate` |
| `queue_health` | Queue totals, alerts, dead letters and queues nothing reads | `monitoring.view` or `dashboard.view` |
| `dashboard` | The Dashboard's numbers | `dashboard.view` |
| `list_subscriptions`, `get_subscription` | Subscriptions, and one's setup, private adapter settings hidden | `subscriptions.view` |
| `pause_subscription`, `resume_subscription` | Pausing and resuming | `subscriptions.operate` |
| `list_partners`, `get_partner` | Partners, secrets and keys hidden | `partners.view` |

Search rows leave out the file links the Exchanges page uses, which open without signing in.

## What's recorded

A change an app makes is in the audit trail under its account, marked `via MCP` for a service-account token
or `<app name> via MCP` for an app signed in with OAuth.

`/mcp` has a request budget per token, the `Bitween:RateLimits:RequestsPerMinute`. The OAuth token and
registration endpoints share the sign-in budget.
