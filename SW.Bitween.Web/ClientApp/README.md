# Bitween admin UI

The web interface for Bitween. It is a React single-page app served by `SW.Bitween.Web` from the site root, and it calls the Bitween API under `/api`.

For what each page does, see [docs/admin-ui.md](../../docs/admin-ui.md).

## Commands

```bash
yarn install
yarn build       # type-check and build into ../wwwroot
yarn test        # Vitest
yarn lint        # oxlint
yarn test:e2e    # Playwright against https://localhost:7155
```

There is no dev-server proxy. Build the UI, run the API with `dotnet run --project SW.Bitween.Web` from the repository root, and open the address it serves. `dotnet publish` runs the build itself unless `-p:SkipClientBuild=true` is passed. The Dockerfile builds the UI in its own stage.

## Stack

React 19, TypeScript, Vite, Tailwind CSS 4, React Router, TanStack Query, CodeMirror, lucide-react, and MSAL for Microsoft sign-in.

## Structure

| Path | Purpose |
|---|---|
| `src/api/` | The only data layer. `client.ts` is the contract, `http/` implements it per area, and `queryKeys.ts` holds cache keys. |
| `src/api/permissions.ts` | Labels for the permission catalogue, which the API serves from `GET /api/permissions`. |
| `src/auth/` | Session context, route and action guards, idle sign-out. |
| `src/nav.ts` | The navigation. The sidebar, the role editor preview and the post-login redirect all derive from it. |
| `src/router.tsx` | Routes and their permission gates. |
| `src/pages/` | One folder per area. `data-sources/` holds data sources, SQL statements and the schema browser. |
| `src/components/config/` | Shared configuration editors: adapters, schedules, match expressions, pickers. |
| `src/components/nativeMapper/`, `src/lib/nativeMapper/` | The rules-based mapping editor. |
| `src/components/mapper/`, `src/lib/mapping/` | The legacy Scriban mapping editor. |
| `e2e/` | Playwright specs, and the `seed` setup project that prepares the database for them. `tools/e2e.sh` runs them against a throwaway instance. |

## Conventions

- Pages and actions a member cannot use are hidden, not disabled.
- State that matters lives in the URL: filters, tabs, selected stages and dialogs.
- Detail pages edit a draft and save from a sticky bar.
- The brand colour comes from the `Theme.PrimaryColor` setting at runtime.
