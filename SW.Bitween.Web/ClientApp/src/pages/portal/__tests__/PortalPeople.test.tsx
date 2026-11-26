import { screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";

const ACME = { id: 7, name: "Acme Freight" };
const ADMIN = ["portal-people.view", "portal-people.edit", "portal-mappings.view", "portal-exchanges.view"];
const person = (id: number, name: string, extra: Record<string, unknown> = {}) => ({
  id, name, email: `${name.toLowerCase()}@acme.test`, role: { id: 2, name: "Partner viewer" }, disabled: false,
  invited: false, hasPassword: true, createdOn: "2026-11-01T10:00:00Z", inviteExpiresOn: null, lastSignInOn: null,
  isYou: false, canChange: true, ...extra,
});
const role = (id: number, name: string, extra: Record<string, unknown> = {}) => ({
  id, name, description: `${name}.`, isSystem: false, forPartners: true, partnerId: null, partnerName: null,
  permissions: ["portal-exchanges.view"], createdOn: "2026-11-01T10:00:00Z", memberCount: 0, ...extra,
});
const CATALOG = [
  { id: "portal-exchanges", label: "Their exchanges", group: "Partner portal", description: "Exchanges.", actions: [{ id: "view", description: "See." }] },
  { id: "portal-mappings", label: "Their mappings", group: "Partner portal", description: "Mappings.", actions: [{ id: "view", description: "See." }, { id: "edit", description: "Change." }] },
  { id: "portal-people", label: "Their people", group: "Partner portal", description: "People.", actions: [{ id: "view", description: "See." }, { id: "edit", description: "Manage." }] },
];

/** Acme's people (the admin looking, another admin, someone they can change) and their roles. */
function backend() {
  const added: unknown[] = [];
  const disabled: unknown[] = [];
  const roleChanges: unknown[] = [];
  const savedRoles: unknown[] = [];
  let people = [
    person(1, "Pat", { isYou: true, canChange: false, role: { id: 4, name: "Partner admin" } }),
    person(2, "Sam", { canChange: false, role: { id: 4, name: "Partner admin" } }),
    person(3, "Lee"),
  ];
  const roles = [
    role(2, "Partner viewer", { isSystem: true }),
    role(3, "Partner editor", { isSystem: true }),
    role(4, "Partner admin", { isSystem: true }),
    role(11, "Warehouse", { partnerId: 7 }),
  ];
  const handlers = [
    http.get(apiPath("/portal/people"), () => HttpResponse.json(people)),
    http.get(apiPath("/portal/roles"), () => HttpResponse.json(roles)),
    http.get(apiPath("/portal/role"), ({ request }) =>
      HttpResponse.json(roles.find((r) => String(r.id) === new URL(request.url).searchParams.get("id"))),
    ),
    http.get(apiPath("/portal/permissions"), () => HttpResponse.json(CATALOG)),
    http.get(apiPath("/portal/peopleroles"), () =>
      HttpResponse.json([
        { id: 2, name: "Partner viewer", description: "Sees.", builtIn: true },
        { id: 11, name: "Warehouse", description: "Theirs.", builtIn: false },
        { id: 3, name: "Partner editor", description: "Sees and changes mappings.", builtIn: true },
      ]),
    ),
    http.post(apiPath("/portal/addperson"), async ({ request }) => {
      const body = (await request.json()) as { name: string };
      added.push(body);
      people = [...people, person(9, body.name, { invited: true, hasPassword: false })];
      return HttpResponse.json({ id: 9, emailed: false, emailError: null, expiresOn: "2026-12-02T10:00:00Z", link: "https://x/set-password?token=abc" });
    }),
    http.post(apiPath("/portal/setpersondisabled"), async ({ request }) => {
      disabled.push(await request.json());
      return new HttpResponse(null, { status: 204 });
    }),
    http.post(apiPath("/portal/setpersonrole"), async ({ request }) => {
      roleChanges.push(await request.json());
      return new HttpResponse(null, { status: 204 });
    }),
    http.post(apiPath("/portal/saverole"), async ({ request }) => {
      savedRoles.push(await request.json());
      return HttpResponse.json(12);
    }),
  ];
  return { handlers, added, disabled, roleChanges, savedRoles };
}

describe("a partner's admin's people, on our own Members page", () => {
  it("lists their company only, and leaves themselves and another admin unchangeable", async () => {
    const { user } = renderApp("/portal/people", { as: { partner: ACME, permissions: ADMIN }, handlers: backend().handlers });

    expect(await screen.findByRole("heading", { name: "People" })).toBeVisible();
    await screen.findByText("Lee");
    expect(screen.queryByText("Belongs to")).not.toBeInTheDocument();

    await user.click(screen.getByText("Sam"));
    const sam = await screen.findByRole("dialog", { name: "Person details" });
    await within(sam).findByText("Partner admin");
    expect(within(sam).queryByRole("button", { name: /Disable account/ })).not.toBeInTheDocument();
    expect(within(sam).queryByRole("radio")).not.toBeInTheDocument();
    // No passwords set by them, no apps, no trail of ours.
    expect(within(sam).queryByText("Connected apps")).not.toBeInTheDocument();
  });

  it("invites someone with a role they may give, and shows the link to pass on", async () => {
    const { handlers, added } = backend();
    const { user } = renderApp("/portal/people", { as: { partner: ACME, permissions: ADMIN }, handlers });

    await user.click(await screen.findByRole("button", { name: /Invite someone/ }));
    const dialog = screen.getByRole("dialog", { name: "Invite someone" });
    expect(within(dialog).queryByText("Who they are")).not.toBeInTheDocument();
    expect(within(dialog).queryByRole("radio", { name: /Partner admin/ })).not.toBeInTheDocument();
    await user.type(within(dialog).getByLabelText("Name"), "Kim");
    await user.type(within(dialog).getByLabelText("Email"), "kim@acme.test");
    await user.click(await within(dialog).findByRole("radio", { name: /Warehouse/ }));
    await user.click(within(dialog).getByRole("button", { name: "Invite" }));

    await waitFor(() => expect(added).toEqual([{ name: "Kim", email: "kim@acme.test", roleId: 11 }]));
    expect(await screen.findByRole("dialog", { name: "Kim is added" })).toHaveTextContent("set-password?token=abc");
  });

  it("changes someone's role and disables them", async () => {
    const { handlers, disabled, roleChanges } = backend();
    const { user } = renderApp("/portal/people/3", { as: { partner: ACME, permissions: ADMIN }, handlers });

    const lee = await screen.findByRole("dialog", { name: "Person details" });
    await user.click(await within(lee).findByRole("radio", { name: /Partner editor/ }));
    await user.click(within(lee).getByRole("button", { name: "Save roles" }));
    await waitFor(() => expect(roleChanges).toEqual([{ accountId: 3, roleId: 3 }]));
    await user.click(within(lee).getByRole("button", { name: /Disable account/ }));
    await waitFor(() => expect(disabled).toEqual([{ accountId: 3, disabled: true }]));
  });

  it("is in the sidebar, with Roles, only for someone who may see their people", async () => {
    renderApp("/portal/mappings", {
      as: { partner: ACME, permissions: ["portal-mappings.view"] },
      handlers: [http.get(apiPath("/portal/mappings"), () => HttpResponse.json([]))],
    });
    const nav = await screen.findByRole("navigation", { name: "Main" });
    expect(within(nav).getByRole("link", { name: "Mappings" })).toBeVisible();
    expect(within(nav).queryByRole("link", { name: "People" })).not.toBeInTheDocument();
    expect(within(nav).queryByRole("link", { name: "Roles" })).not.toBeInTheDocument();
  });
});

describe("a partner's admin's roles, on our own Roles page", () => {
  it("shows ours as ready-made and theirs as theirs to change", async () => {
    const { user } = renderApp("/portal/roles", { as: { partner: ACME, permissions: ADMIN }, handlers: backend().handlers });

    const viewer = (await screen.findByRole("heading", { name: "Partner viewer" })).closest("a")!;
    expect(within(viewer).getByText("Ready-made")).toBeVisible();
    const warehouse = screen.getByRole("heading", { name: "Warehouse" }).closest("a")!;
    expect(within(warehouse).queryByText("Ready-made")).not.toBeInTheDocument();
    expect(warehouse).toHaveAttribute("href", "/portal/roles/11");

    await user.click(viewer);
    expect(await screen.findByText(/We made this role/)).toBeVisible();
    expect(screen.getByRole("checkbox", { name: "Their exchanges: View" })).toBeDisabled();
  });

  it("makes a role of their own, never managing people, and only with what they hold", async () => {
    const { handlers, savedRoles } = backend();
    const { user } = renderApp("/portal/roles/new", { as: { partner: ACME, permissions: ADMIN }, handlers });

    await user.type(await screen.findByLabelText("Name"), "Night shift");
    expect(screen.queryByText("Who it's for")).not.toBeInTheDocument();
    expect(screen.getByRole("checkbox", { name: "Their people: Edit" })).toBeDisabled();
    // They hold mappings view, not edit.
    expect(screen.getByRole("checkbox", { name: "Their mappings: Edit" })).toBeDisabled();
    await user.click(screen.getByRole("checkbox", { name: "Their exchanges: View" }));
    await user.click(screen.getByRole("button", { name: "Create role" }));

    await waitFor(() =>
      expect(savedRoles).toEqual([{ name: "Night shift", description: "", permissions: ["portal-exchanges.view"] }]),
    );
  });
});
