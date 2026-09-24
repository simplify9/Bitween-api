import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse, type RequestHandler } from "msw";
import { RouterProvider, createMemoryRouter } from "react-router";
import { TOKEN_KEY } from "../../api";
import type { AppConfig } from "../../api/http/appConfig";
import { SessionProvider } from "../../auth/SessionContext";
import { routes } from "../../router";
import { server } from "./server";

/**
 * Every permission key there is, read from the C# catalog rather than copied, so an administrator
 * in these tests holds exactly what a real one would.
 */
export const ALL_PERMISSIONS = [
  // From the project root: under jsdom, import.meta.url is a page URL rather than a file.
  ...readFileSync(resolve(process.cwd(), "../../SW.Bitween.Sdk/Model/Permissions.cs"), "utf8")
    .matchAll(/public const string \w+ = "([a-z-]+\.[a-z-]+)"/g),
].map((m) => m[1]);

/** A path on the API, matched whatever origin the page runs under. */
export const apiPath = (path: string) => `*/api${path}`;

export const ADMIN = { id: 9999, email: "admin@test.local", name: "Test Admin" };

export interface Signed {
  id?: number;
  email?: string;
  name?: string;
  permissions?: string[];
  roles?: { id: number; name: string }[];
}

/** The profile the app loads its session from. */
export const profile = (who: Signed = {}) =>
  http.get(apiPath("/accounts/profile"), () =>
    HttpResponse.json({
      id: who.id ?? ADMIN.id,
      email: who.email ?? ADMIN.email,
      name: who.name ?? ADMIN.name,
      role: "Admin",
      disabled: false,
      createdOn: "2026-01-01T00:00:00Z",
      roles: who.roles ?? [{ id: 1, name: "Administrator" }],
      permissions: who.permissions ?? ALL_PERMISSIONS,
    }),
  );

/** The pre-sign-in configuration: branding and which sign-in methods exist. */
export const appConfig = (config: AppConfig = {}) =>
  http.get(apiPath("/settings/config"), () => HttpResponse.json(config));

export interface RenderAppOptions {
  /** Who is signed in, or `null` for nobody. Defaults to an administrator. */
  as?: Signed | null;
  config?: AppConfig;
  /** Whatever else the page asks the API for. */
  handlers?: RequestHandler[];
}

/**
 * Mounts the whole app — session, router, data layer — at one URL, against the mock network.
 *
 * The real routes and the real API client run; only the server is fake. So a test here reads like
 * the Playwright spec it replaced, minus the backend and the database that made those slow and
 * dependent on whatever data happened to be lying around.
 */
export function renderApp(path: string, { as = {}, config = {}, handlers = [] }: RenderAppOptions = {}) {
  if (as) localStorage.setItem(TOKEN_KEY, "test-token");
  server.use(...handlers, appConfig(config), ...(as ? [profile(as)] : []));

  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const router = createMemoryRouter(routes, { initialEntries: [path] });

  render(
    <QueryClientProvider client={queryClient}>
      <SessionProvider>
        <RouterProvider router={router} />
      </SessionProvider>
    </QueryClientProvider>,
  );

  return { user: userEvent.setup(), router, queryClient };
}
