# Bitween UI — redesign prototype

Clean-slate redesign of the Bitween admin UI, hosted by `SW.Bitween.Web`. **Runs entirely on
mock data** — it never calls the API, even when served by it. Sub-phase 1 covers auth, dynamic
RBAC, user profile, and team management; other areas exist as permission-gated placeholder
pages so role gating can be demonstrated across the whole navigation.

## Running it

**UI-only (fastest loop):**

```bash
npm install
npm run dev        # → http://localhost:5173/bitween/
```

**Backend-served (how it deploys):**

```bash
npm run build      # outputs into ../wwwroot with base /bitween/
dotnet run --project .. --launch-profile SW.Bitween.Web.Local
# → https://localhost:7155/bitween/
```

`dotnet publish` runs the npm build automatically (skip with `-p:SkipClientBuild=true`);
the root `Dockerfile` builds the UI in its own node stage. The app lives under the host's
`/bitween` path base — runtime URLs must use `import.meta.env.BASE_URL` (see the project
instructions in `.github/instructions/`).

Sign in with any prototype account listed on the login page (password `bitween`), or use the
one-click persona buttons. The floating **Demo** pill (bottom right) switches the signed-in
person at any time and resets the demo data.

## Stack

React 19 · TypeScript · Vite · Tailwind v4 · react-router v8 · TanStack Query · lucide-react.

Brand carried over from the existing UI: the crimson ramp from `Bitween-UI/tailwind.config.js`
(verbatim, as `--color-crimson-*`) and the logo (`public/brand/`). Neutrals (`--color-ink-*`)
are derived from the logo's wordmark ink `#372f2e`. Tokens live in `src/index.css`.

## Architecture — the parts that matter

- **`src/api/` is the only data layer.** Components import `api` from `src/api/index.ts`,
  which today points at `src/api/mock/mockClient.ts` (a localStorage-backed fake with
  simulated latency). Implementing `ApiClient` (`src/api/client.ts`) over HTTP and changing
  that one export swaps the whole app onto the real backend.
- **`src/api/permissions.ts` is the permission catalog** — every gated area and action.
  Roles are named sets of these keys; a user's permissions are the union of their roles'.
- **`src/nav.ts` is the information architecture.** Sidebar, role-editor access preview and
  post-login redirect all derive from it, each filtered by the session's permissions.
- **Gating hides, never dims**: `<Can>` for actions, `<RequirePermission>` for routes
  (`src/auth/guards.tsx`). Unauthorized pages get an explanatory access-denied screen.
- **Everything is URL-addressable**: tabs are routes, the member drawer is a route, search
  and filters are query params, dialogs are query params.
- Deployment assumptions (to revisit): served at root path, single project, client-side
  routing compatible with being backend-served later.

Anything the prototype needed that the real backend can't do yet is logged in
`Bitween-api/BACKEND_CAPABILITIES_NEEDED.md`.
