import { screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it, vi } from "vitest";
import { ALL_PERMISSIONS, apiPath, renderApp } from "../../../__tests__/support/renderApp";

/**
 * The Adapters page: built-in and installed adapters in their own tabs, with versions and usage,
 * and the marketplace tab listing what the registry set under Settings offers.
 */

/** GET /adapters/Catalog, per kind: `RawCatalogAdapter` in src/api/http/adapters.ts. */
const SMTP = {
  key: "NativeSmtpHandler",
  native: true,
  versions: [],
  startupValues: { Host: { optional: false, default: null, private: false, description: "Mail server" } },
  displayName: "Email (SMTP)",
  summary: "Sends the message as an email.",
  publisher: "Simplify9",
  tags: ["email"],
  versionHistory: [],
  currentVersion: null,
};
const ORDERS = {
  key: "acme.handlers.orders",
  native: false,
  versions: ["1.1.0"],
  startupValues: { Token: { optional: false, default: null, private: true, description: null } },
  displayName: "Acme orders",
  summary: "Sends orders to Acme.",
  publisher: "Acme Ltd",
  tags: ["orders"],
  currentVersion: "1.1.0",
  versionHistory: [
    {
      version: "1.0.0",
      publishedOn: "2026-09-01T00:00:00Z",
      publishedBy: "ci",
      releaseNotes: "First cut",
      withdrawn: true,
      runtime: "dotnet",
    },
    {
      version: "1.1.0",
      publishedOn: "2026-10-01T00:00:00Z",
      publishedBy: "ci",
      releaseNotes: "Retries on 503",
      withdrawn: false,
      runtime: "python",
    },
  ],
};
/** Published by an older installer: no manifest, no catalog, only version files. */
const LEGACY = { key: "infolink6.receivers.ftp", native: false, versions: ["2.0.0"], startupValues: {} };

/** GET /subscriptions: RawSubscription rows, just the fields this page reads. */
const sub = (id: number, name: string, fields: Record<string, unknown>) => ({
  id,
  name,
  type: "BusGateway",
  documentId: 3,
  partnerId: null,
  receiverId: null,
  validatorId: null,
  mapperId: null,
  handlerId: null,
  receiverProperties: [],
  validatorProperties: [],
  mapperProperties: [],
  handlerProperties: [],
  ...fields,
});

const handlers = [
  // The editor's drafts, listed for whoever may write adapters: none here.
  http.get(apiPath("/adapterdrafts"), () => HttpResponse.json([])),
  http.get(apiPath("/adapters/Catalog"), ({ request }) => {
    const prefix = new URL(request.url).searchParams.get("prefix");
    if (prefix === "handlers") return HttpResponse.json([SMTP, ORDERS]);
    if (prefix === "receivers") return HttpResponse.json([LEGACY]);
    return HttpResponse.json([]);
  }),
  http.get(apiPath("/subscriptions"), () =>
    HttpResponse.json({
      result: [
        sub(1, "Pinned orders", { handlerId: "acme.handlers.orders", handlerVersion: "1.1.0" }),
        sub(2, "Current orders", { handlerId: "acme.handlers.orders" }),
        sub(3, "Mailer", { handlerId: "NativeSmtpHandler" }),
      ],
      totalCount: 3,
    }),
  ),
];

// The page waits on four catalog requests and the subscriptions list before it draws anything,
// which takes longer than findBy's default second on a busy machine.
const LOADED = { timeout: 5000 };

const section = async (name: string) =>
  within((await screen.findByRole("heading", { name: new RegExp(`^${name}`) }, LOADED)).closest("section")!);

describe("the Adapters page", () => {
  it("lists built-in and installed adapters in their own tabs, with how many each holds", async () => {
    const { user } = renderApp("/adapters", { handlers });

    expect(await screen.findByRole("tab", { name: /^Built-in \d+$/ }, LOADED)).toHaveAttribute("aria-selected", "true");
    expect(screen.getByText("Email (SMTP)")).toBeVisible();
    expect(screen.queryByText("Acme orders")).not.toBeInTheDocument();

    await user.click(screen.getByRole("tab", { name: /^Installed/ }));
    expect(screen.queryByText("Email (SMTP)")).not.toBeInTheDocument();
    const custom = await section("Published");
    expect(custom.getByText("Acme orders")).toBeVisible();
    expect(custom.getByText("v1.1.0")).toBeVisible();
    expect(custom.getByText(/by Acme Ltd/)).toBeVisible();
    expect(custom.getByText("Used by 2 subscriptions")).toBeVisible();
    // Withdrawn versions are not counted as available.
    expect(custom.getAllByText("1 version")[0]).toBeVisible();
    // An adapter from before manifests is still listed, by its id.
    expect(custom.getByText("infolink6.receivers.ftp", { selector: "code" })).toBeVisible();
  });

  it("shows a custom adapter's versions, notes and pins when opened", async () => {
    const { user } = renderApp("/adapters?tab=installed", { handlers });

    await user.click(await screen.findByRole("button", { name: /Acme orders/ }, LOADED));

    const versions = screen.getByRole("heading", { name: "Versions" }).parentElement!;
    const rows = within(versions).getAllByRole("row").slice(1);
    expect(rows.map((r) => r.textContent)).toEqual([
      expect.stringContaining("v1.1.0Current"),
      expect.stringContaining("v1.0.0Withdrawn"),
    ]);
    expect(within(rows[0]).getByText("Retries on 503")).toBeVisible();
    const cells = within(rows[0]).getAllByRole("cell");
    expect(cells[1]).toHaveTextContent("Python");
    expect(within(rows[1]).getAllByRole("cell")[1]).toHaveTextContent(".NET");
    expect(cells[3]).toHaveTextContent(/^1$/); // pinned by one
    expect(within(versions).getByText("1 use follows the current version.")).toBeVisible();

    expect(screen.getByText("Token")).toBeVisible();
    expect(screen.getByText("Secret")).toBeVisible();
    expect(screen.getByRole("link", { name: "Pinned orders" })).toHaveAttribute("href", "/subscriptions/1");
  });

  it("filters by kind and search, in each tab", async () => {
    const { user } = renderApp("/adapters", { handlers });

    await user.click(await screen.findByRole("radio", { name: "Receivers" }, LOADED));
    expect(screen.getByText("No adapter matches.")).toBeVisible();

    await user.click(screen.getByRole("radio", { name: "All" }));
    await user.type(screen.getByRole("searchbox", { name: "Search adapters" }), "email");
    expect(screen.getByText("Email (SMTP)")).toBeVisible();

    await user.click(screen.getByRole("tab", { name: /^Installed/ }));
    await user.type(screen.getByRole("searchbox", { name: "Search adapters" }), "email");
    expect((await section("Published")).getByText("No adapter matches.")).toBeVisible();
  });

  it("shows what each custom adapter runs on, and filters by it", async () => {
    const { user } = renderApp("/adapters?tab=installed", {
      handlers: [
        http.get(apiPath("/adapterdrafts"), () => HttpResponse.json([])),
        http.get(apiPath("/adapters/Catalog"), ({ request }) =>
          HttpResponse.json(
            new URL(request.url).searchParams.get("prefix") === "handlers"
              ? [
                  SMTP,
                  ORDERS,
                  {
                    ...ORDERS,
                    key: "acme.handlers.invoices",
                    displayName: "Acme invoices",
                    versionHistory: [{ ...ORDERS.versionHistory[1], runtime: "node" }],
                  },
                ]
              : [],
          ),
        ),
        handlers[2],
      ],
    });

    const custom = await section("Published");
    // The current version's runtime, not the withdrawn .NET one's.
    expect(within(custom.getByRole("button", { name: /Acme orders/ })).getByText("Python")).toBeVisible();
    expect(within(custom.getByRole("button", { name: /Acme invoices/ })).getByText("Node.js")).toBeVisible();

    await user.click(screen.getByRole("radio", { name: "Node.js" }));
    expect((await section("Published")).queryByText("Acme orders")).not.toBeInTheDocument();
    expect((await section("Published")).getByText("Acme invoices")).toBeVisible();

    // Built-in adapters have no runtime of their own, so their tab doesn't offer the filter.
    await user.click(screen.getByRole("tab", { name: /^Built-in/ }));
    expect(screen.queryByRole("radio", { name: "Node.js" })).not.toBeInTheDocument();
  });

  it("withdraws a version that isn't current, saying what happens to its pins", async () => {
    const posts: Array<{ path: string; body: unknown }> = [];
    const withThree = {
      ...ORDERS,
      versions: ["1.1.0", "1.2.0"],
      versionHistory: [
        ...ORDERS.versionHistory,
        {
          version: "1.2.0",
          publishedOn: "2026-10-05T00:00:00Z",
          publishedBy: "ci",
          releaseNotes: "Beta",
          withdrawn: false,
        },
      ],
    };
    const { user } = renderApp("/adapters?tab=installed", {
      handlers: [
        http.get(apiPath("/adapters/Catalog"), ({ request }) =>
          HttpResponse.json(new URL(request.url).searchParams.get("prefix") === "handlers" ? [withThree] : []),
        ),
        http.post(apiPath("/adapters/withdraw"), async ({ request }) => {
          posts.push({ path: "withdraw", body: await request.json() });
          return HttpResponse.json({ adapterId: "acme.handlers.orders", withdrawn: "1.2.0" });
        }),
        ...handlers,
      ],
    });

    await user.click(await screen.findByRole("button", { name: /Acme orders/ }, LOADED));
    // The current version can't be withdrawn, and a withdrawn one can't be again.
    expect(screen.queryByRole("button", { name: "Withdraw v1.1.0" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Withdraw v1.0.0" })).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Withdraw v1.2.0" }));
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText(/can't be pinned or made current again/)).toBeVisible();
    await user.click(within(dialog).getByRole("button", { name: "Withdraw v1.2.0" }));

    await vi.waitFor(() =>
      expect(posts).toEqual([{ path: "withdraw", body: { adapterId: "acme.handlers.orders", version: "1.2.0" } }]),
    );
  });

  it("publishes an uploaded package, current only when asked", async () => {
    let received: { query: string; type: string | null } | null = null;
    const { user } = renderApp("/adapters", {
      handlers: [
        http.post(apiPath("/adapters/packages"), async ({ request }) => {
          received = {
            query: new URL(request.url).search,
            type: request.headers.get("content-type"),
          };
          return HttpResponse.json({ adapterId: "acme.orders", version: "0.2.0", current: true, sha256: "x" });
        }),
        ...handlers,
      ],
    });

    await user.click(await screen.findByRole("button", { name: "Upload package" }, LOADED));
    const dialog = await screen.findByRole("dialog");
    await user.upload(
      within(dialog).getByLabelText("Package"),
      new File(["PK-zip-bytes"], "acme.orders-0.2.0.zip", { type: "application/zip" }),
    );
    await user.selectOptions(within(dialog).getByLabelText("Version"), "minor");
    await user.click(within(dialog).getByRole("checkbox"));
    await user.click(within(dialog).getByRole("button", { name: "Publish" }));

    expect(await within(dialog).findByText(/and made it current/)).toBeVisible();
    // Behind the dialog, the page has moved to where the package now is.
    await waitFor(() =>
      expect(screen.getByRole("tab", { name: /^Installed/ })).toHaveAttribute("aria-selected", "true"),
    );
    // The bytes themselves aren't checked: jsdom's File isn't a Blob Node's fetch can send, which a
    // browser's is. The package goes as the body, typed as a zip, with the choices in the query.
    expect(received).toMatchObject({ query: "?version=minor&current=true", type: "application/zip" });
  });

  it("says when an administrator turned the marketplace off", async () => {
    const { user } = renderApp("/adapters", {
      handlers: [
        ...handlers,
        http.get(apiPath("/marketplace/search"), () => HttpResponse.json({ configured: false, disabled: true })),
      ],
    });

    await user.click(await screen.findByRole("tab", { name: "Marketplace" }, LOADED));

    expect(screen.getByRole("tab", { name: "Marketplace" })).toHaveAttribute("aria-selected", "true");
    expect(await screen.findByText("The marketplace is turned off")).toBeVisible();
  });

  it("says when no registry is set", async () => {
    renderApp("/adapters?tab=marketplace", {
      handlers: [...handlers, http.get(apiPath("/marketplace/search"), () => HttpResponse.json({ configured: false, disabled: false }))],
    });

    expect(await screen.findByText("No registry is set")).toBeVisible();
  });

  it("installs a kit with its parameters, showing the plan first", async () => {
    const card = {
      id: "s9.salesforce.contacts",
      displayName: "Salesforce contacts",
      summary: "Contacts both ways.",
      publisher: "s9",
      publisherName: "Simplify9",
      tier: "Official",
      pricing: "Free",
      latest: "1.0.0",
      runtime: null,
      kinds: "kit",
      categories: ["CRM"],
      iconDataUri: null,
      downloads: 0,
      updatedOn: "2026-10-13T00:00:00Z",
    };
    const installed = { installed: false, fromRegistry: false, current: null, versions: [] };
    const sent: Array<{ path: string; body: unknown }> = [];
    const run = (applied: boolean) => ({
      name: "s9.salesforce.contacts", ready: true, applied, problems: [], warnings: [], changes: 2,
      lines: ['Warning: connection "salesforce" is approved once: choose Authorize.', '  + information type "Salesforce contact"'],
    });
    const { user } = renderApp("/adapters?tab=marketplace", {
      handlers: [
        ...handlers,
        http.get(apiPath("/marketplace/search"), () =>
          HttpResponse.json({ configured: true, registry: "https://registry.bitween.io", totalCount: 1, result: [{ card, installed }] }),
        ),
        http.get(apiPath("/marketplace/kit"), () =>
          HttpResponse.json({
            id: card.id, version: "1.0.0", displayName: card.displayName, summary: card.summary, description: null,
            connectors: [{ id: "s9.openapi", version: "1.0.0" }],
            parameters: [
              { name: "InstanceUrl", description: "Your org", required: true, secret: false, default: null, example: "https://acme.my.salesforce.com" },
              { name: "ClientSecret", description: "The secret", required: true, secret: true, default: null, example: null },
              { name: "Label", description: "Names", required: true, secret: false, default: "Salesforce", example: null },
            ],
          }),
        ),
        http.post(apiPath("/marketplace/kitplan"), async ({ request }) => {
          sent.push({ path: "plan", body: await request.json() });
          return HttpResponse.json(run(false));
        }),
        http.post(apiPath("/marketplace/kitinstall"), async ({ request }) => {
          sent.push({ path: "install", body: await request.json() });
          return HttpResponse.json(run(true));
        }),
      ],
    });

    await user.click(await screen.findByRole("button", { name: /Salesforce contacts/ }));
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByRole("button", { name: "Install" })).toBeDisabled();
    await user.type(within(dialog).getByLabelText("InstanceUrl"), "https://acme.my.salesforce.com");
    await user.type(within(dialog).getByLabelText("ClientSecret"), "s3cret");
    expect(within(dialog).getByLabelText("ClientSecret")).toHaveAttribute("type", "password");
    expect(within(dialog).getByLabelText("Label")).toHaveValue("Salesforce");

    await user.click(within(dialog).getByRole("button", { name: "Show plan" }));
    expect(await within(dialog).findByText(/information type "Salesforce contact"/)).toBeVisible();
    await user.click(within(dialog).getByRole("button", { name: "Install" }));
    expect(await within(dialog).findByText("Installed as s9.salesforce.contacts.")).toBeVisible();

    expect(sent.map((x) => x.path)).toEqual(["plan", "install"]);
    expect(sent[1].body).toEqual({
      id: "s9.salesforce.contacts", version: "1.0.0",
      parameters: { InstanceUrl: "https://acme.my.salesforce.com", ClientSecret: "s3cret", Label: "Salesforce" },
    });
  });

  it("lists the registry's connectors and installs one", async () => {
    const card = {
      id: "s9.openapi",
      displayName: "OpenAPI",
      summary: "Calls any operation of an API described by OpenAPI.",
      publisher: "s9",
      publisherName: "Simplify9",
      tier: "Official",
      pricing: "Free",
      latest: "1.1.0",
      runtime: "python",
      kinds: "handler",
      categories: ["Protocols"],
      iconDataUri: null,
      downloads: 3,
      updatedOn: "2026-10-12T00:00:00Z",
    };
    const installed = { installed: true, fromRegistry: true, current: "1.0.0", versions: ["1.0.0"] };
    let body: unknown;
    const { user } = renderApp("/adapters?tab=marketplace", {
      handlers: [
        ...handlers,
        http.get(apiPath("/marketplace/search"), () =>
          HttpResponse.json({ configured: true, registry: "https://registry.test", result: [{ card, installed }], totalCount: 1 }),
        ),
        http.get(apiPath("/marketplace/package"), () =>
          HttpResponse.json({
            package: {
              card,
              description: null,
              homepage: null,
              license: "MIT",
              versions: [
                { version: "1.1.0", status: "Published", size: 10, minBitweenVersion: "10.0.0", signedOn: "2026-10-12T00:00:00Z", revokeReason: null },
                { version: "1.0.0", status: "Published", size: 10, minBitweenVersion: "10.0.0", signedOn: "2026-10-01T00:00:00Z", revokeReason: null },
              ],
            },
            latest: { properties: [{ name: "SpecUrl", description: "Where the OpenAPI document is" }] },
            installed,
          }),
        ),
        http.post(apiPath("/marketplace/install"), async ({ request }) => {
          body = await request.json();
          return HttpResponse.json({ adapterId: "s9.openapi", version: "1.1.0", current: true, sha256: "x", publisher: "s9" });
        }),
      ],
    });

    // Installed from the registry, with a newer version there.
    expect(await screen.findByText("Update available")).toBeVisible();
    await user.click(screen.getByRole("button", { name: /OpenAPI/ }));

    const dialog = await screen.findByRole("dialog");
    expect(await within(dialog).findByText("SpecUrl")).toBeVisible();
    await user.click(within(dialog).getByRole("button", { name: "Install 1.1.0" }));

    expect(await within(dialog).findByText(/Installed s9.openapi 1.1.0/)).toBeVisible();
    expect(body).toEqual({ id: "s9.openapi", version: "1.1.0", current: true });
  });

  it("is in the sidebar for whoever can see subscriptions, and only for them", async () => {
    renderApp("/adapters", { handlers });
    expect(await screen.findByRole("link", { name: "Adapters" })).toHaveAttribute("href", "/adapters");
  });

  it("is refused without the subscriptions permission", async () => {
    renderApp("/adapters", {
      handlers: [...handlers, http.get(apiPath("/permissions"), () => HttpResponse.json([]))],
      as: { permissions: ALL_PERMISSIONS.filter((p) => p !== "subscriptions.view") },
    });
    expect(await screen.findByRole("navigation", { name: "Main" })).toBeVisible();
    expect(screen.queryByRole("link", { name: "Adapters" })).not.toBeInTheDocument();
    expect(screen.queryByRole("tab", { name: /^Built-in/ })).not.toBeInTheDocument();
  });

  it("shows each adapter's use, and its errors and versions when opened", async () => {
    const totals = {
      calls: 1200,
      failures: 12,
      failureRate: 1,
      timeouts: 3,
      limitStops: 0,
      crashes: 1,
      p50Ms: 90,
      p95Ms: 450,
      maxMs: 2_900,
    };
    const { user } = renderApp("/adapters?tab=installed", {
      handlers: [
        http.get(apiPath("/metrics/adapters"), () =>
          HttpResponse.json({
            since: "2026-10-01T00:00:00Z",
            days: 7,
            adapters: [
              {
                id: "acme.handlers.orders",
                ...totals,
                lastCalledOn: "2026-10-11T08:00:00Z",
                daily: [100, 150, 200, 180, 170, 190, 210].map((calls, i) => ({
                  day: `2026-10-0${i + 5}T00:00:00Z`,
                  calls,
                  failures: 1,
                })),
              },
            ],
            quiet: [],
          }),
        ),
        http.get(apiPath("/metrics/adapter"), () =>
          HttpResponse.json({
            id: "acme.handlers.orders",
            days: 7,
            since: "2026-10-01T00:00:00Z",
            total: totals,
            versions: [{ version: "1.1.0", totals }],
            nodes: [{ node: "web-1:42", totals }],
            roles: [{ role: "handler", runtime: "process", totals }],
            daily: [],
            errors: [
              {
                fingerprint: "ab12",
                count: 9,
                lastSeenOn: "2026-10-11T07:00:00Z",
                sample: "Acme answered 503 Service Unavailable",
              },
            ],
            subscriptions: [
              {
                subscriptionId: 1,
                name: "Pinned orders",
                type: "BusGateway",
                exchanges: 1100,
                failed: 10,
                bytes: 2_500_000,
              },
            ],
          }),
        ),
        ...handlers,
      ],
    });

    const row = await screen.findByRole("button", { name: /Acme orders/ }, LOADED);
    expect(within(row).getByText(/1.2K calls/)).toBeVisible();
    expect(within(row).getByText(/1% failed/)).toBeVisible();
    expect(within(row).getByRole("img", { name: "Calls per day, last 7 days" })).toBeInTheDocument();

    await user.click(row);
    expect(await screen.findByText("Acme answered 503 Service Unavailable")).toBeVisible();
    expect(screen.getByText(/3 timed out/)).toBeVisible();
    expect(within(screen.getByRole("table", { name: "By version" })).getByText("v1.1.0")).toBeVisible();
    expect(screen.getByText("2.4 MB")).toBeVisible();
  });
});
