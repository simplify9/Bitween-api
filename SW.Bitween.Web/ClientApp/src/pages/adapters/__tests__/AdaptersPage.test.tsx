import { screen, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it, vi } from "vitest";
import { ALL_PERMISSIONS, apiPath, renderApp } from "../../../__tests__/support/renderApp";

/**
 * The Adapters page: built-in and installed adapters in their own tabs, with versions and usage,
 * and the marketplace tab waiting for its content.
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
    { version: "1.0.0", publishedOn: "2026-09-01T00:00:00Z", publishedBy: "ci", releaseNotes: "First cut", withdrawn: true, runtime: "dotnet" },
    { version: "1.1.0", publishedOn: "2026-10-01T00:00:00Z", publishedBy: "ci", releaseNotes: "Retries on 503", withdrawn: false, runtime: "python" },
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
        { version: "1.2.0", publishedOn: "2026-10-05T00:00:00Z", publishedBy: "ci", releaseNotes: "Beta", withdrawn: false },
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

    await vi.waitFor(() => expect(posts).toEqual([{ path: "withdraw", body: { adapterId: "acme.handlers.orders", version: "1.2.0" } }]));
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
    await user.upload(within(dialog).getByLabelText("Package"), new File(["PK-zip-bytes"], "acme.orders-0.2.0.zip", { type: "application/zip" }));
    await user.selectOptions(within(dialog).getByLabelText("Version"), "minor");
    await user.click(within(dialog).getByRole("checkbox"));
    await user.click(within(dialog).getByRole("button", { name: "Publish" }));

    expect(await within(dialog).findByText(/and made it current/)).toBeVisible();
    // Behind the dialog, the page has moved to where the package now is.
    expect(screen.getByRole("tab", { name: /^Installed/ })).toHaveAttribute("aria-selected", "true");
    // The bytes themselves aren't checked: jsdom's File isn't a Blob Node's fetch can send, which a
    // browser's is. The package goes as the body, typed as a zip, with the choices in the query.
    expect(received).toMatchObject({ query: "?version=minor&current=true", type: "application/zip" });
  });

  it("has a marketplace tab, still to come", async () => {
    const { user } = renderApp("/adapters", { handlers });

    await user.click(await screen.findByRole("tab", { name: "Marketplace" }, LOADED));

    expect(screen.getByRole("tab", { name: "Marketplace" })).toHaveAttribute("aria-selected", "true");
    expect(screen.getByText("The marketplace is on its way")).toBeVisible();
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
});
