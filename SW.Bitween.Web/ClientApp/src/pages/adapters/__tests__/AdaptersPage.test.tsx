import { screen, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { ALL_PERMISSIONS, apiPath, renderApp } from "../../../__tests__/support/renderApp";

/**
 * The Adapters page: built-in and custom adapters in their own sections, with versions and usage,
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
    { version: "1.0.0", publishedOn: "2026-09-01T00:00:00Z", publishedBy: "ci", releaseNotes: "First cut", withdrawn: true },
    { version: "1.1.0", publishedOn: "2026-10-01T00:00:00Z", publishedBy: "ci", releaseNotes: "Retries on 503", withdrawn: false },
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
  it("lists built-in and custom adapters in their own sections", async () => {
    renderApp("/adapters", { handlers });

    const builtIn = await section("Built-in");
    expect(builtIn.getByText("Email (SMTP)")).toBeVisible();
    expect(builtIn.queryByText("Acme orders")).not.toBeInTheDocument();

    const custom = await section("Custom");
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
    const { user } = renderApp("/adapters", { handlers });

    await user.click(await screen.findByRole("button", { name: /Acme orders/ }, LOADED));

    const versions = screen.getByRole("heading", { name: "Versions" }).parentElement!;
    const rows = within(versions).getAllByRole("row").slice(1);
    expect(rows.map((r) => r.textContent)).toEqual([
      expect.stringContaining("v1.1.0Current"),
      expect.stringContaining("v1.0.0Withdrawn"),
    ]);
    expect(within(rows[0]).getByText("Retries on 503")).toBeVisible();
    expect(within(rows[0]).getAllByRole("cell")[2]).toHaveTextContent("1"); // pinned by one
    expect(within(versions).getByText("1 use follows the current version.")).toBeVisible();

    expect(screen.getByText("Token")).toBeVisible();
    expect(screen.getByText("Secret")).toBeVisible();
    expect(screen.getByRole("link", { name: "Pinned orders" })).toHaveAttribute("href", "/subscriptions/1");
  });

  it("filters by kind and search", async () => {
    const { user } = renderApp("/adapters", { handlers });

    await user.click(await screen.findByRole("radio", { name: "Receivers" }, LOADED));
    expect((await section("Custom")).queryByText("Acme orders")).not.toBeInTheDocument();
    expect((await section("Built-in")).getByText("No built-in adapter matches.")).toBeVisible();

    await user.click(screen.getByRole("radio", { name: "All" }));
    await user.type(screen.getByRole("searchbox", { name: "Search adapters" }), "email");
    expect((await section("Built-in")).getByText("Email (SMTP)")).toBeVisible();
    expect((await section("Custom")).getByText("No custom adapter matches.")).toBeVisible();
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
    expect(await screen.findByRole("navigation")).toBeVisible();
    expect(screen.queryByRole("link", { name: "Adapters" })).not.toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: /^Built-in/ })).not.toBeInTheDocument();
  });
});
