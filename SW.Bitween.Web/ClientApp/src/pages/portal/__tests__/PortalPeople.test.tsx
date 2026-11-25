import { screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";

const ACME = { id: 7, name: "Acme Freight" };
const ADMIN = ["portal-people.view", "portal-people.edit", "portal-mappings.view"];
const person = (id: number, name: string, extra: Record<string, unknown> = {}) => ({
  id, name, email: `${name.toLowerCase()}@acme.test`, role: { id: 2, name: "Partner viewer" }, disabled: false,
  invited: false, inviteExpiresOn: null, lastSignInOn: null, isYou: false, canChange: true, ...extra,
});

/** Acme's people: the admin looking, another admin, and someone they can change. */
function backend() {
  const added: unknown[] = [];
  const disabled: unknown[] = [];
  const roleChanges: unknown[] = [];
  let people = [
    person(1, "Pat", { isYou: true, canChange: false, role: { id: 4, name: "Partner admin" } }),
    person(2, "Sam", { canChange: false, role: { id: 4, name: "Partner admin" } }),
    person(3, "Lee"),
  ];
  const handlers = [
    http.get(apiPath("/portal/people"), () => HttpResponse.json(people)),
    http.get(apiPath("/portal/peopleroles"), () =>
      HttpResponse.json([
        { id: 2, name: "Partner viewer", description: "Sees.", builtIn: true },
        { id: 3, name: "Partner editor", description: "Sees and changes mappings.", builtIn: true },
      ]),
    ),
    http.post(apiPath("/portal/addperson"), async ({ request }) => {
      const body = (await request.json()) as { name: string };
      added.push(body);
      people = [...people, person(9, body.name, { invited: true })];
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
  ];
  return { handlers, added, disabled, roleChanges };
}

describe("a partner's admin's people page", () => {
  it("lists their company, and leaves themselves and another admin unchangeable", async () => {
    renderApp("/portal/people", { as: { partner: ACME, permissions: ADMIN }, handlers: backend().handlers });

    const lee = (await screen.findByText("Lee")).closest("tr")!;
    expect(within(lee).getByRole("combobox", { name: "Role of Lee" })).toBeVisible();
    for (const name of ["Pat", "Sam"]) {
      const row = screen.getByText(name).closest("tr")!;
      expect(within(row).queryByRole("button", { name: "Remove" })).not.toBeInTheDocument();
      expect(within(row).getByText("Partner admin")).toBeVisible();
    }
  });

  it("invites someone and shows the link to pass on when there's no mail server", async () => {
    const { handlers, added } = backend();
    const { user } = renderApp("/portal/people", { as: { partner: ACME, permissions: ADMIN }, handlers });

    await user.click(await screen.findByRole("button", { name: /Invite someone/ }));
    const dialog = screen.getByRole("dialog", { name: "Invite someone" });
    await user.type(within(dialog).getByLabelText("Name"), "Kim");
    await user.type(within(dialog).getByLabelText(/Email/), "kim@acme.test");
    await user.selectOptions(within(dialog).getByLabelText(/Role/), "3");
    await user.click(within(dialog).getByRole("button", { name: "Invite" }));

    await waitFor(() => expect(added).toEqual([{ name: "Kim", email: "kim@acme.test", roleId: 3 }]));
    expect(await screen.findByRole("dialog", { name: "Pass on this invite" })).toHaveTextContent("set-password?token=abc");
  });

  it("changes someone's role and disables them", async () => {
    const { handlers, disabled, roleChanges } = backend();
    const { user } = renderApp("/portal/people", { as: { partner: ACME, permissions: ADMIN }, handlers });

    await user.selectOptions(await screen.findByRole("combobox", { name: "Role of Lee" }), "3");
    await waitFor(() => expect(roleChanges).toEqual([{ accountId: 3, roleId: 3 }]));
    const lee = screen.getByText("Lee").closest("tr")!;
    await user.click(within(lee).getByRole("button", { name: "Disable" }));
    await waitFor(() => expect(disabled).toEqual([{ accountId: 3, disabled: true }]));
  });

  it("is in the sidebar only for someone who may see their people", async () => {
    renderApp("/portal/mappings", {
      as: { partner: ACME, permissions: ["portal-mappings.view"] },
      handlers: [http.get(apiPath("/portal/mappings"), () => HttpResponse.json([]))],
    });
    const nav = await screen.findByRole("navigation", { name: "Main" });
    expect(within(nav).getByRole("link", { name: "Mappings" })).toBeVisible();
    expect(within(nav).queryByRole("link", { name: "People" })).not.toBeInTheDocument();
  });
});
