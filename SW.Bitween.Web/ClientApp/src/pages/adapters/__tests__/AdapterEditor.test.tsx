import { screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { ALL_PERMISSIONS, apiPath, renderApp } from "../../../__tests__/support/renderApp";

/**
 * Writing an adapter in Bitween: starting one, the editor's save, check, try and publish, and the
 * Edit and Make current actions on published versions — against the API's real routes and shapes.
 */

const DRAFT = {
  id: 7,
  adapterId: "acme.orders",
  language: "python",
  kind: "handler",
  baseVersion: null,
  filesHash: "h",
  createdOn: "2026-10-09T10:00:00Z",
  createdBy: "Admin",
  modifiedOn: null,
  modifiedBy: null,
  files: {
    "adapter.json": '{ "id": "acme.orders", "runtime": "python", "entry": "main.py" }\n',
    "main.py": "import sw_serverless as sw\n",
  },
};

const BUILT = {
  succeeded: true,
  conforms: true,
  problems: [],
  warnings: [],
  checks: [
    { name: "manifest", outcome: "Passed", detail: null },
    { name: "bitween handler: Handle answers example 1", outcome: "Passed", detail: "a valid ExchangeFile" },
  ],
  settings: [
    { name: "BaseUrl", description: "Where messages go.", required: false, secret: false, default: "https://partner.example.test", type: "text" },
    { name: "ApiKey", description: "The partner's key.", required: true, secret: true, default: null, type: "text" },
  ],
  commands: ["Handle"],
  lifecycle: "classic",
  kinds: ["handler"],
};

const calls: { path: string; body: unknown }[] = [];
const record = (path: string) => async ({ request }: { request: Request }) => {
  calls.push({ path, body: request.method === "POST" ? await request.json().catch(() => null) : null });
};

const editorHandlers = [
  http.get(apiPath("/adapterdrafts/7"), () => HttpResponse.json(DRAFT)),
  http.post(apiPath("/adapterdrafts/7"), async (info) => {
    await record("save")(info);
    return HttpResponse.json(DRAFT);
  }),
  http.post(apiPath("/adapterdrafts/7/build"), async (info) => {
    await record("build")(info);
    return HttpResponse.json(BUILT);
  }),
  http.post(apiPath("/adapterdrafts/7/try"), async (info) => {
    await record("try")(info);
    return HttpResponse.json({ succeeded: true, output: '{"Data":"hello","BadData":false}', error: null, problems: [] });
  }),
  http.post(apiPath("/adapterdrafts/7/publish"), async (info) => {
    await record("publish")(info);
    return HttpResponse.json({ published: true, version: "1.0.0", build: BUILT });
  }),
  http.post(apiPath("/adapters/promote"), async (info) => {
    await record("promote")(info);
    return HttpResponse.json({ adapterId: "acme.orders", current: "1.0.0" });
  }),
  http.get(apiPath("/adapterdrafts"), () => HttpResponse.json([DRAFT])),
  http.get(apiPath("/adapters/Catalog"), () => HttpResponse.json([])),
  http.get(apiPath("/subscriptions"), () => HttpResponse.json({ result: [], totalCount: 0 })),
];

const LOADED = { timeout: 5000 };

describe("the adapter editor", () => {
  it("starts a new adapter from the dialog and opens it", async () => {
    calls.length = 0;
    const { user, router } = renderApp("/adapters", {
      handlers: [
        http.post(apiPath("/adapterdrafts"), async (info) => {
          await record("create")(info);
          return HttpResponse.json(7);
        }),
        ...editorHandlers,
      ],
    });

    await user.click(await screen.findByRole("button", { name: "New adapter" }, LOADED));
    const dialog = screen.getByRole("dialog", { name: "New adapter" });
    await user.type(within(dialog).getByLabelText("Name"), "AcmeOrders");
    await user.selectOptions(within(dialog).getByLabelText("Language"), "typescript");
    await user.selectOptions(within(dialog).getByLabelText("Kind"), "validator");
    await user.click(within(dialog).getByRole("button", { name: "Start writing" }));

    await waitFor(() => expect(router.state.location.pathname).toBe("/adapters/drafts/7"));
    expect(calls.find((c) => c.path === "create")?.body).toEqual({ name: "AcmeOrders", language: "typescript", kind: "validator" });
    expect(await screen.findByRole("heading", { name: "acme.orders" }, LOADED)).toBeVisible();
  });

  it("lists drafts on the Adapters page", async () => {
    renderApp("/adapters", { handlers: editorHandlers });
    const drafts = (await screen.findByRole("heading", { name: /^Drafts/ }, LOADED)).closest("section")!;
    expect(within(drafts).getByRole("link", { name: /acme\.orders/ })).toHaveAttribute("href", "/adapters/drafts/7");
  });

  it("saves, checks with settings, tries, publishes and makes the version current", async () => {
    calls.length = 0;
    const { user } = renderApp("/adapters/drafts/7", { handlers: editorHandlers });

    const files = await screen.findByRole("region", { name: "Files" }, LOADED);
    expect(within(files).getAllByRole("button", { name: /^[^ ]+\.(json|py)$/ }).map((b) => b.textContent)).toEqual(["adapter.json", "main.py"]);
    expect(screen.getByTestId("draft-editor")).toHaveAttribute("data-path", "main.py");

    // A new file makes the draft unsaved; checking saves it first.
    await user.click(within(files).getByRole("button", { name: "New file" }));
    await user.type(screen.getByLabelText("New file name"), "helpers.py{Enter}");
    expect(screen.getByText("Unsaved")).toBeVisible();

    await user.click(screen.getByRole("button", { name: "Save and check" }));
    await screen.findByText("Builds and conforms.", undefined, LOADED);
    const saved = calls.find((c) => c.path === "save")?.body as { files: Record<string, string> };
    expect(Object.keys(saved.files).sort()).toEqual(["adapter.json", "helpers.py", "main.py"]);
    expect(screen.queryByText("Unsaved")).not.toBeInTheDocument();

    // The settings the adapter declares appear, secrets masked; what is typed goes with each call.
    const key = screen.getByLabelText("ApiKey *");
    expect(key).toHaveAttribute("type", "password");
    await user.type(key, "k-1");
    await user.click(screen.getByRole("button", { name: "Save and check" }));
    await waitFor(() => expect(calls.filter((c) => c.path === "build")).toHaveLength(2));
    expect(calls.filter((c) => c.path === "build")[1].body).toEqual({ settings: { ApiKey: "k-1" }, buildOnly: false });

    await user.click(screen.getByRole("tab", { name: "Try" }));
    expect(screen.getByLabelText("Command")).toHaveValue("Handle");
    await user.click(screen.getByRole("button", { name: "Save and run" }));
    const result = await screen.findByLabelText("Try result", undefined, LOADED);
    expect(result).toHaveTextContent('"Data": "hello"');
    expect(calls.find((c) => c.path === "try")?.body).toMatchObject({ command: "Handle", settings: { ApiKey: "k-1" } });

    await user.click(screen.getByRole("tab", { name: "Publish" }));
    expect(screen.getByLabelText("Version number")).toHaveValue("1.0.0");
    await user.type(screen.getByLabelText("Release notes"), "First cut");
    await user.click(screen.getByRole("button", { name: "Publish" }));
    const published = await screen.findByLabelText("Published", undefined, LOADED);
    expect(published).toHaveTextContent("Published v1.0.0.");
    expect(published).toHaveTextContent("It isn't current yet");
    expect(calls.find((c) => c.path === "publish")?.body).toEqual({ version: "1.0.0", releaseNotes: "First cut", settings: { ApiKey: "k-1" } });

    await user.click(within(published).getByRole("button", { name: "Make v1.0.0 current" }));
    await waitFor(() => expect(published).toHaveTextContent("v1.0.0 is now current"));
    expect(calls.find((c) => c.path === "promote")?.body).toEqual({ adapterId: "acme.orders", version: "1.0.0" });
  });

  it("shows what a failed check found", async () => {
    const { user } = renderApp("/adapters/drafts/7", {
      handlers: [
        http.post(apiPath("/adapterdrafts/7/build"), () =>
          HttpResponse.json({ ...BUILT, succeeded: false, conforms: false, checks: [], settings: null, problems: ["--describe exited with 1: SyntaxError: invalid syntax"] }),
        ),
        ...editorHandlers,
      ],
    });
    await user.click(await screen.findByRole("button", { name: "Save and check" }, LOADED));
    expect(await screen.findByText(/SyntaxError: invalid syntax/, undefined, LOADED)).toBeVisible();
  });

  it("has no Publish tab without adapter-source.operate", async () => {
    renderApp("/adapters/drafts/7", {
      handlers: editorHandlers,
      as: { permissions: ALL_PERMISSIONS.filter((p) => p !== "adapter-source.operate") },
    });
    expect(await screen.findByRole("tab", { name: "Check" }, LOADED)).toBeVisible();
    expect(screen.queryByRole("tab", { name: "Publish" })).not.toBeInTheDocument();
  });

  it("is refused, with no New adapter button, without adapter-source.edit", async () => {
    const permissions = ALL_PERMISSIONS.filter((p) => p !== "adapter-source.edit");
    renderApp("/adapters", { handlers: editorHandlers, as: { permissions } });
    expect(await screen.findByRole("heading", { name: /^Built-in/ }, LOADED)).toBeVisible();
    expect(screen.queryByRole("button", { name: "New adapter" })).not.toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: /^Drafts/ })).not.toBeInTheDocument();
  });
});

describe("published versions", () => {
  const ORDERS = {
    key: "acme.handlers.orders",
    native: false,
    versions: ["1.0.0", "1.1.0"],
    startupValues: {},
    displayName: "Acme orders",
    currentVersion: "1.0.0",
    versionHistory: [
      { version: "1.0.0", publishedOn: "2026-09-01T00:00:00Z", withdrawn: false, hasSource: true, runtime: "python" },
      { version: "1.1.0", publishedOn: "2026-10-01T00:00:00Z", withdrawn: false, hasSource: true, runtime: "python" },
    ],
  };
  const handlers = [
    http.get(apiPath("/adapters/Catalog"), ({ request }) =>
      HttpResponse.json(new URL(request.url).searchParams.get("prefix") === "handlers" ? [ORDERS] : []),
    ),
    ...editorHandlers.filter((h) => !String(h.info.path).endsWith("/adapters/Catalog")),
  ];

  it("can be edited, or made current, by whoever may", async () => {
    calls.length = 0;
    const { user, router } = renderApp("/adapters", {
      handlers: [
        http.post(apiPath("/adapterdrafts"), async (info) => {
          await record("create")(info);
          return HttpResponse.json(7);
        }),
        ...handlers,
      ],
    });
    await user.click(await screen.findByRole("button", { name: /Acme orders/ }, LOADED));

    // The current version is already current.
    expect(screen.queryByRole("button", { name: "Make v1.0.0 current" })).not.toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Make v1.1.0 current" }));
    const confirm = screen.getByRole("dialog", { name: "Make v1.1.0 current?" });
    expect(confirm).toHaveTextContent("make v1.0.0 current again");
    await user.click(within(confirm).getByRole("button", { name: "Make v1.1.0 current" }));
    await waitFor(() => expect(calls.find((c) => c.path === "promote")?.body).toEqual({ adapterId: "acme.handlers.orders", version: "1.1.0" }));

    await user.click(screen.getByRole("button", { name: "Edit v1.1.0" }));
    await waitFor(() => expect(router.state.location.pathname).toBe("/adapters/drafts/7"));
    expect(calls.find((c) => c.path === "create")?.body).toEqual({ fromAdapterId: "acme.handlers.orders", fromVersion: "1.1.0" });
  });

  it("offers neither to a member without the permissions", async () => {
    const permissions = ALL_PERMISSIONS.filter((p) => !p.startsWith("adapter-source."));
    const { user } = renderApp("/adapters", { handlers, as: { permissions } });
    await user.click(await screen.findByRole("button", { name: /Acme orders/ }, LOADED));
    expect(screen.queryByRole("button", { name: /^Edit v/ })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /current$/ })).not.toBeInTheDocument();
  });

  it("offers no Edit for a .NET version", async () => {
    const dotnet = { ...ORDERS, versionHistory: ORDERS.versionHistory.map((v) => ({ ...v, runtime: "dotnet" })) };
    const { user } = renderApp("/adapters", {
      handlers: [
        http.get(apiPath("/adapters/Catalog"), ({ request }) =>
          HttpResponse.json(new URL(request.url).searchParams.get("prefix") === "handlers" ? [dotnet] : []),
        ),
        ...handlers,
      ],
    });
    await user.click(await screen.findByRole("button", { name: /Acme orders/ }, LOADED));
    expect(screen.getByRole("button", { name: "Make v1.1.0 current" })).toBeVisible();
    expect(screen.queryByRole("button", { name: /^Edit v/ })).not.toBeInTheDocument();
  });
});
