import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";

/**
 * The catalog GET /permissions serves, read out of PermissionCatalog.Areas in the C# rather than
 * copied: the matrix renders whatever the server sends, so a hand-made one would only test itself.
 */
const CATALOG = readFileSync(resolve(process.cwd(), "../../SW.Bitween.Sdk/Model/Permissions.cs"), "utf8")
  .split("List<PermissionAreaModel> Areas =")[1]
  .split("];")[0]
  .replace(/\/\/.*$/gm, "")
  .split("Area(")
  .slice(1)
  .map((area) => {
    const strings = [...area.matchAll(/"([^"]*)"/g)].map((m) => m[1]);
    const actions = [...area.matchAll(/\((View|Create|Edit|Delete|Operate), "([^"]*)"\)/g)].map((m) => ({
      id: m[1].toLowerCase(),
      description: m[2],
    }));
    const [id, label, group, ...rest] = strings;
    // What's left once the actions' own descriptions are off the end is the area's, which the C#
    // sometimes splits across concatenated literals.
    return { id, label, group, description: rest.slice(0, rest.length - actions.length).join(""), actions };
  });

/** A role as GET /roles and GET /roles/{id} return it (RoleRow). */
const role = (id: number, name: string, permissions: string[], isSystem = false) => ({
  id,
  name,
  description: isSystem ? "Full access to everything, including members, roles and settings." : "",
  isSystem,
  permissions,
  memberCount: isSystem ? 1 : 0,
  createdOn: "2026-01-01T00:00:00Z",
});

// A built-in role's grants are computed on read, and Administrator's are the whole catalog.
const ADMINISTRATOR = role(1, "Administrator", CATALOG.flatMap((a) => a.actions.map((x) => `${a.id}.${x.id}`)), true);

/** The roles, held the way the server holds them so a created one comes back in the list. */
function roles(...custom: ReturnType<typeof role>[]) {
  const all = [ADMINISTRATOR, ...custom];
  const created: unknown[] = [];
  const handlers = [
    http.get(apiPath("/permissions"), () => HttpResponse.json(CATALOG)),
    http.get(apiPath("/roles"), () => HttpResponse.json({ result: all, totalCount: all.length })),
    http.get(apiPath("/roles/:id"), ({ params }) => HttpResponse.json(all.find((r) => r.id === Number(params.id)))),
    http.post(apiPath("/roles"), async ({ request }) => {
      const body = (await request.json()) as { name: string; description: string; permissions: string[] };
      created.push(body);
      const id = Math.max(...all.map((r) => r.id)) + 1;
      all.push({ ...role(id, body.name, body.permissions), description: body.description });
      return HttpResponse.json(id);
    }),
    // A custom role's History card.
    http.get(apiPath("/audit"), () => HttpResponse.json({ result: [], totalCount: 0 })),
  ];
  return { handlers, created };
}

const box = (name: string) => screen.getByRole("checkbox", { name });

describe("the role editor", () => {
  it("grants View along with any action, and takes the row with it when View is cleared", async () => {
    const { user } = renderApp("/team/roles/new", { handlers: roles().handlers });

    // An action you can't view is an action you can't reach, so View comes along.
    const edit = await screen.findByRole("checkbox", { name: "Partners: Edit" });
    const view = box("Partners: View");
    const del = box("Partners: Delete");

    // Only the count granted is asserted, not the catalog size — that changes whenever a
    // permission is added or dropped, and it isn't what this test is about.
    const granted = (n: number) => new RegExp(`\\b${n}/\\d+ permissions granted`);

    await user.click(edit);
    expect(view).toBeChecked();
    expect(screen.getByText(granted(2))).toBeVisible();

    await user.click(del);
    expect(screen.getByText(granted(3))).toBeVisible();

    // Removing View takes the whole area with it.
    await user.click(view);
    expect(edit).not.toBeChecked();
    expect(del).not.toBeChecked();
    expect(screen.getByText(granted(0))).toBeVisible();
  });

  it("previews what members with the role would see", async () => {
    const { user } = renderApp("/team/roles/new", { handlers: roles().handlers });

    expect(await screen.findByText("No pages yet — grant a View permission.")).toBeVisible();
    // Scoped to the preview: the signed-in admin's own sidebar lists every page too.
    const preview = within(screen.getByText("What members with this role see").closest("div")!);

    await user.click(box("Partners: View"));
    expect(preview.getByText("Partners")).toBeVisible();
    expect(preview.queryByText("Exchanges")).not.toBeInTheDocument();

    await user.click(box("Exchanges: View"));
    expect(preview.getByText("Exchanges")).toBeVisible();
  });

  it("shows a built-in role read-only", async () => {
    const { user } = renderApp("/team/roles", { handlers: roles().handlers });

    await user.click(await screen.findByRole("link", { name: /Administrator/ }));

    expect(await screen.findByText(/This role is built in/)).toBeVisible();
    expect(box("Partners: View")).toBeDisabled();
    expect(screen.queryByRole("button", { name: "Delete role" })).not.toBeInTheDocument();
    // Name and description aren't even rendered for a built-in.
    expect(document.querySelector("#role-name")).not.toBeInTheDocument();
  });

  it("duplicates a role", async () => {
    const { handlers, created } = roles(role(7, "Operator", ["partners.view", "partners.edit"]));
    const { user, router } = renderApp("/team/roles", { handlers });

    await user.click(await screen.findByRole("link", { name: /Operator/ }));
    await user.click(await screen.findByRole("button", { name: "Duplicate" }));

    const name = await screen.findByDisplayValue("Copy of Operator");
    expect(name).toHaveAttribute("id", "role-name");
    expect(box("Partners: Edit")).toBeChecked();

    await user.clear(name);
    await user.type(name, "Night operator");
    await user.click(screen.getByRole("button", { name: "Create role" }));
    await waitFor(() => expect(router.state.location.pathname).toBe("/team/roles"));

    // A new role in its own right, carrying the source's grants — not an edit of the source.
    expect(created).toEqual([{ name: "Night operator", description: "", permissions: ["partners.view", "partners.edit"] }]);
    expect(await screen.findByRole("link", { name: /Night operator/ })).toBeVisible();
  });
});
