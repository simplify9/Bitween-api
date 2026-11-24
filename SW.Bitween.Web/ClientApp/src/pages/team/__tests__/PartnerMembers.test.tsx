import { screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { ADMIN, apiPath, renderApp } from "../../../__tests__/support/renderApp";

const role = (id: number, name: string, forPartners = false, isSystem = id <= 3) => ({
  id,
  name,
  description: "",
  isSystem,
  forPartners,
  permissions: [],
  memberCount: 0,
  createdOn: "2026-01-01T00:00:00Z",
});
const ROLES = [role(1, "Administrator"), role(3, "Viewer"), role(41, "Partner viewer", true, true), role(40, "Acme viewers", true)];

const account = (id: number, name: string, partner: { id: number; name: string } | null, roleIds: number[]) => ({
  id,
  name,
  email: `${name.toLowerCase().replace(/\W/g, "")}@test.local`,
  role: "Viewer",
  disabled: false,
  lockoutEnd: null,
  createdOn: "2026-01-01T00:00:00Z",
  hasPassword: true,
  roles: ROLES.filter((r) => roleIds.includes(r.id)).map(({ id, name }) => ({ id, name })),
  partnerId: partner?.id ?? null,
  partnerName: partner?.name ?? null,
});

function team() {
  const accounts = [account(ADMIN.id, ADMIN.name, null, [1]), account(50, "Dana at Acme", { id: 7, name: "Acme Freight" }, [40])];
  const created: Record<string, unknown>[] = [];
  const handlers = [
    http.get(apiPath("/accounts/connectedapps"), () => HttpResponse.json([])),
    http.get(apiPath("/accounts"), () => HttpResponse.json({ result: accounts, totalCount: accounts.length })),
    http.get(apiPath("/roles"), () => HttpResponse.json({ result: ROLES, totalCount: ROLES.length })),
    http.get(apiPath("/audit"), () => HttpResponse.json({ result: [], totalCount: 0 })),
    http.get(apiPath("/partners"), () =>
      HttpResponse.json({ result: [{ id: 7, name: "Acme Freight", subscriptionsCount: 0, keys: 0, propertyKeys: [] }], totalCount: 1 }),
    ),
    http.post(apiPath("/accounts"), async ({ request }) => {
      const body = (await request.json()) as Record<string, unknown>;
      created.push(body);
      accounts.push(account(51, String(body.name), { id: 7, name: "Acme Freight" }, body.roleIds as number[]));
      return HttpResponse.json(51);
    }),
    http.post(apiPath("/accounts/:id/invite"), () =>
      HttpResponse.json({ emailed: false, emailError: null, expiresOn: null, link: "https://bitween.test/set-password?token=t" }),
    ),
  ];
  return { handlers, created };
}

describe("a partner's people on the Team page", () => {
  it("lists who belongs to a partner", async () => {
    renderApp("/team/members", { handlers: team().handlers });
    const dana = await screen.findByRole("row", { name: /Dana at Acme/ });
    expect(within(dana).getByText("Acme Freight")).toBeVisible();
    expect(within(screen.getByRole("row", { name: new RegExp(ADMIN.email) })).getByText("Our team")).toBeVisible();
  });

  it("adds one with the partner and a partner role, offering only partner roles", async () => {
    const { handlers, created } = team();
    const { user } = renderApp("/team/members?add=1", { handlers });
    const dialog = within(await screen.findByRole("dialog", { name: "Add a member" }));

    await user.type(dialog.getByLabelText("Name"), "Sam at Acme");
    await user.type(dialog.getByLabelText("Email"), "sam@acme.test");
    expect(dialog.getByRole("radio", { name: /^Viewer/ })).toBeChecked();

    await user.click(dialog.getByRole("radio", { name: "A partner's person" }));
    expect(dialog.queryByRole("radio", { name: /^Viewer/ })).not.toBeInTheDocument();
    // The built-in partner role is ready to use, picked for them.
    expect(dialog.getByRole("radio", { name: /^Partner viewer/ })).toBeChecked();
    await user.click(dialog.getByRole("combobox", { name: "Partner" }));
    await user.click(await screen.findByRole("option", { name: /Acme Freight/ }));
    await user.click(dialog.getByRole("radio", { name: /Acme viewers/ }));
    await user.click(dialog.getByRole("button", { name: /Add and/ }));

    await waitFor(() => expect(created).toHaveLength(1));
    expect(created[0]).toMatchObject({ name: "Sam at Acme", partnerId: 7, roleIds: [40], invite: true });
  });

  it("offers a partner's person only partner roles in their drawer", async () => {
    const { user } = renderApp("/team/members", { handlers: team().handlers });
    await user.click(await screen.findByRole("row", { name: /Dana at Acme/ }));
    const drawer = within(screen.getByRole("dialog", { name: "Member details" }));
    expect(await drawer.findByRole("radio", { name: /Acme viewers/ })).toBeChecked();
    expect(drawer.queryByRole("radio", { name: /Administrator/ })).not.toBeInTheDocument();
  });

  it("asks someone holding several roles from before to choose one", async () => {
    const { handlers } = team();
    const both = account(52, "Two Roles", null, [1, 3]);
    const { user } = renderApp("/team/members", {
      handlers: [http.get(apiPath("/accounts"), () => HttpResponse.json({ result: [both], totalCount: 1 })), ...handlers],
    });
    await user.click(await screen.findByRole("row", { name: /Two Roles/ }));
    const drawer = within(screen.getByRole("dialog", { name: "Member details" }));
    expect(await drawer.findByText(/Holds Administrator and Viewer from before/)).toBeVisible();
    expect(drawer.getByRole("radio", { name: /^Viewer/ })).not.toBeChecked();
  });
});
