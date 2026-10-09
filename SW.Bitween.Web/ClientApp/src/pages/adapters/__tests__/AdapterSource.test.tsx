import { screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { ALL_PERMISSIONS, apiPath, renderApp } from "../../../__tests__/support/renderApp";
import { compareListings, languageFor } from "../../../lib/adapterSource";

/**
 * Reading an adapter version's source on the Adapters page, and comparing two versions — for any
 * language, since the server only hands over files and hashes.
 */

const ORDERS = {
  key: "acme.handlers.orders",
  native: false,
  versions: ["1.0.0", "1.1.0", "1.2.0"],
  startupValues: {},
  displayName: "Acme orders",
  currentVersion: "1.2.0",
  versionHistory: [
    // Published before packages carried source.
    { version: "1.0.0", publishedOn: "2026-09-01T00:00:00Z", withdrawn: false },
    { version: "1.1.0", publishedOn: "2026-09-15T00:00:00Z", withdrawn: false, hasSource: true },
    { version: "1.2.0", publishedOn: "2026-10-01T00:00:00Z", withdrawn: false, hasSource: true },
  ],
};

const FILES: Record<string, Record<string, string>> = {
  "1.1.0": {
    "Handler.cs": "class Handler { int retries = 1; }\n",
    "Adapter.csproj": "<Project />\n",
    "Old.cs": "class Old {}\n",
  },
  "1.2.0": {
    "Handler.cs": "class Handler { int retries = 3; }\n",
    "Adapter.csproj": "<Project />\n",
    "Retry.cs": "class Retry {}\n",
  },
};
// Hashes only need to differ when the content does.
const sha = (text: string) => `h${text.length}-${[...text].reduce((a, c) => (a * 31 + c.charCodeAt(0)) >>> 0, 7)}`;

const reads: string[] = [];
const handlers = [
  http.get(apiPath("/adapters/Catalog"), ({ request }) =>
    HttpResponse.json(new URL(request.url).searchParams.get("prefix") === "handlers" ? [ORDERS] : []),
  ),
  http.get(apiPath("/subscriptions"), () => HttpResponse.json({ result: [], totalCount: 0 })),
  http.get(apiPath("/adapters/source"), ({ request }) => {
    const version = new URL(request.url).searchParams.get("version")!;
    return HttpResponse.json({
      adapterId: ORDERS.key,
      version,
      language: "csharp",
      runtime: "dotnet",
      buildCommand: "dotnet publish -c Release",
      lockfiles: [],
      files: Object.entries(FILES[version] ?? {})
        .map(([path, text]) => ({ path, sha256: sha(text) }))
        .sort((a, b) => a.path.localeCompare(b.path)),
    });
  }),
  http.get(apiPath("/adapters/sourcefile"), ({ request }) => {
    const q = new URL(request.url).searchParams;
    const text = FILES[q.get("version")!]?.[q.get("path")!];
    reads.push(`${q.get("version")}:${q.get("path")}`);
    if (text === undefined) return HttpResponse.json({ message: "not found" }, { status: 404 });
    return HttpResponse.json({ path: q.get("path"), sha256: sha(text), size: text.length, binary: false, content: text });
  }),
];

const LOADED = { timeout: 5000 };

async function openVersions(user: ReturnType<typeof renderApp>["user"]) {
  await user.click(await screen.findByRole("button", { name: /Acme orders/ }, LOADED));
  return screen.getByRole("heading", { name: "Versions" }).parentElement!;
}

const editor = () => screen.findByTestId("source-editor", undefined, LOADED);

describe("adapter source", () => {
  it("offers source only for versions that carry it", async () => {
    const { user } = renderApp("/adapters", { handlers });
    const versions = await openVersions(user);

    expect(within(versions).getByRole("button", { name: "View source of v1.2.0" })).toBeVisible();
    expect(within(versions).getByRole("button", { name: "View source of v1.1.0" })).toBeVisible();
    expect(within(versions).queryByRole("button", { name: "View source of v1.0.0" })).not.toBeInTheDocument();
  });

  it("shows a version's files and the first file's content", async () => {
    reads.length = 0;
    const { user } = renderApp("/adapters", { handlers });
    const versions = await openVersions(user);
    await user.click(within(versions).getByRole("button", { name: "View source of v1.2.0" }));

    const files = await screen.findByRole("list", { name: "Source files" }, LOADED);
    expect(within(files).getAllByRole("button").map((b) => b.textContent)).toEqual([
      "Adapter.csproj",
      "Handler.cs",
      "Retry.cs",
    ]);
    expect(screen.getByText("dotnet publish -c Release")).toBeVisible();

    await waitFor(async () => expect(await editor()).toHaveAttribute("data-path", "Adapter.csproj"));
    await user.click(within(files).getByRole("button", { name: "Handler.cs" }));
    await waitFor(async () => expect((await editor()).textContent).toContain("int retries = 3;"));
    expect(await editor()).toHaveAttribute("data-diff", "false");
    expect(reads).toContain("1.2.0:Handler.cs");
  });

  it("compares two versions: what was added, removed and changed, and the change itself", async () => {
    reads.length = 0;
    const { user } = renderApp("/adapters", { handlers });
    const versions = await openVersions(user);
    await user.click(within(versions).getByRole("button", { name: "View source of v1.2.0" }));
    await screen.findByRole("list", { name: "Source files" }, LOADED);

    await user.selectOptions(screen.getByRole("combobox", { name: "Compare with" }), "1.1.0");

    const files = screen.getByRole("list", { name: "Source files" });
    // Unchanged files are left out until asked for.
    await waitFor(() =>
      expect(within(files).getAllByRole("button").map((b) => b.textContent)).toEqual([
        "Handler.cschanged",
        "Old.csremoved",
        "Retry.csadded",
      ]),
    );

    await waitFor(async () => expect(await editor()).toHaveAttribute("data-diff", "true"));
    const shown = await editor();
    expect(shown).toHaveAttribute("data-path", "Handler.cs");
    // The unified diff shows the new line and, deleted, the old one.
    await waitFor(() => expect(shown.textContent).toContain("int retries = 3;"));
    expect(shown.textContent).toContain("int retries = 1;");
    expect(reads).toEqual(expect.arrayContaining(["1.2.0:Handler.cs", "1.1.0:Handler.cs"]));

    // An added file has nothing to read from before; a removed one nothing from after.
    reads.length = 0;
    await user.click(within(files).getByRole("button", { name: /Retry\.cs/ }));
    await waitFor(async () => expect((await editor()).textContent).toContain("class Retry"));
    expect(reads).toEqual(["1.2.0:Retry.cs"]);

    reads.length = 0;
    await user.click(within(files).getByRole("button", { name: /Old\.cs/ }));
    await waitFor(async () => expect((await editor()).textContent).toContain("class Old"));
    expect(reads).toEqual(["1.1.0:Old.cs"]);

    await user.click(screen.getByRole("checkbox", { name: "Changed files only" }));
    expect(within(files).getAllByRole("button")).toHaveLength(4);
  });

  it("closes", async () => {
    const { user } = renderApp("/adapters", { handlers });
    const versions = await openVersions(user);
    await user.click(within(versions).getByRole("button", { name: "View source of v1.1.0" }));
    await screen.findByRole("list", { name: "Source files" }, LOADED);

    await user.click(screen.getByRole("button", { name: "Close source" }));
    expect(screen.queryByRole("list", { name: "Source files" })).not.toBeInTheDocument();
  });

  it("is not offered without adapter-source.view", async () => {
    const { user } = renderApp("/adapters", {
      handlers,
      as: { permissions: ALL_PERMISSIONS.filter((p) => p !== "adapter-source.view") },
    });
    const versions = await openVersions(user);

    expect(within(versions).queryByRole("button", { name: /View source/ })).not.toBeInTheDocument();
    expect(within(versions).queryByRole("columnheader", { name: "Source" })).not.toBeInTheDocument();
  });

  it("shows nothing extra for a server that predates source", async () => {
    const legacy = { ...ORDERS, versionHistory: ORDERS.versionHistory.map(({ hasSource: _, ...v }) => v) };
    const { user } = renderApp("/adapters", {
      handlers: [
        http.get(apiPath("/adapters/Catalog"), ({ request }) =>
          HttpResponse.json(new URL(request.url).searchParams.get("prefix") === "handlers" ? [legacy] : []),
        ),
        ...handlers,
      ],
    });
    const versions = await openVersions(user);
    expect(within(versions).queryByRole("columnheader", { name: "Source" })).not.toBeInTheDocument();
  });
});

describe("compareListings", () => {
  const listing = (files: Record<string, string>) => ({
    adapterId: "a",
    version: "1.0.0",
    language: null,
    runtime: null,
    buildCommand: null,
    lockfiles: [],
    files: Object.entries(files).map(([path, sha256]) => ({ path, sha256 })),
  });

  it("marks nothing without a base", () => {
    expect(compareListings(listing({ "a.py": "1" }), null)).toEqual([{ path: "a.py", change: null }]);
  });

  it("tells files apart by hash", () => {
    expect(compareListings(listing({ "a.py": "1", "b.py": "2", "d.py": "4" }), listing({ "a.py": "1", "b.py": "x", "c.py": "3" }))).toEqual([
      { path: "a.py", change: "unchanged" },
      { path: "b.py", change: "changed" },
      { path: "c.py", change: "removed" },
      { path: "d.py", change: "added" },
    ]);
  });
});

describe("languageFor", () => {
  it.each(["Handler.cs", "main.py", "index.ts", "index.js", "x.tsx", "package.json", "Adapter.csproj", "main.go", "lib.rs", "a.yaml", "pyproject.toml"])(
    "highlights %s",
    (path) => expect(languageFor(path)).not.toBeNull(),
  );

  it("leaves unknown files as text", () => {
    expect(languageFor("README")).toBeNull();
    expect(languageFor("notes.txt")).toBeNull();
  });
});
