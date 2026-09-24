import { screen, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { ADMIN, apiPath, renderApp } from "../../../__tests__/support/renderApp";

/** The built-in roles, as the server seeds them (BitweenDbContext.SystemRoleSeed). */
const ROLES = [
  { id: 1, name: "Administrator", description: "Full access to everything, including members, roles and settings." },
  { id: 2, name: "Member", description: "Runs and configures integrations. Can't manage members, roles or settings." },
  { id: 3, name: "Viewer", description: "Read-only access to integrations, exchanges and configuration." },
].map((r) => ({ ...r, isSystem: true, permissions: [], memberCount: 0, createdOn: "2026-01-01T00:00:00Z" }));

/** An account as the member list returns it (AccountModel). */
const account = (id: number, name: string, email: string, roleIds: number[], disabled = false) => ({
  id,
  name,
  email,
  role: "Member",
  disabled,
  lockoutEnd: null,
  createdOn: "2026-01-01T00:00:00Z",
  roles: ROLES.filter((r) => roleIds.includes(r.id)).map(({ id, name }) => ({ id, name })),
});

const self = () => account(ADMIN.id, ADMIN.name, ADMIN.email, [1]);

/**
 * The accounts held the way the server holds them, so a write is answered and then read back
 * through the member list. The page re-reads after every save rather than trusting what it sent,
 * and these tests should see it do that. What each write carried is kept in `sent`.
 */
function team(...accounts: ReturnType<typeof account>[]) {
  const sent: Record<string, unknown[]> = { setRoles: [], setDisabled: [] };
  const find = (id: unknown) => accounts.find((a) => a.id === Number(id))!;
  const handlers = [
    http.get(apiPath("/accounts"), () => HttpResponse.json({ result: accounts, totalCount: accounts.length })),
    http.get(apiPath("/roles"), () => HttpResponse.json({ result: ROLES, totalCount: ROLES.length })),
    // The drawer's History section.
    http.get(apiPath("/audit"), () => HttpResponse.json({ result: [], totalCount: 0 })),
    http.post(apiPath("/accounts/:id/setRoles"), async ({ params, request }) => {
      const body = (await request.json()) as { roleIds: number[] };
      sent.setRoles.push(body);
      find(params.id).roles = account(0, "", "", body.roleIds).roles;
      return new HttpResponse(null, { status: 204 });
    }),
    http.post(apiPath("/accounts/:id/setDisabled"), async ({ params, request }) => {
      const body = (await request.json()) as { disabled: boolean };
      sent.setDisabled.push(body);
      find(params.id).disabled = body.disabled;
      return new HttpResponse(null, { status: 204 });
    }),
  ];
  return { handlers, sent };
}

const row = (email: string) => screen.getByRole("row", { name: new RegExp(email) });
const drawer = () => screen.getByRole("dialog", { name: "Member details" });

describe("the member list", () => {
  it("changes which roles a member holds", async () => {
    const { handlers, sent } = team(self(), account(42, "Role Swap", "role.swap@test.local", [3]));
    const { user } = renderApp("/team/members", { handlers });

    await user.click(await screen.findByRole("row", { name: /role.swap@test.local/ }));
    const panel = within(drawer());
    await user.click(await panel.findByRole("checkbox", { name: /^Viewer/ }));
    await user.click(panel.getByRole("checkbox", { name: /^Member/ }));
    await user.click(panel.getByRole("button", { name: "Save roles" }));
    await expect.poll(() => panel.queryByRole("button", { name: "Save roles" })).toBeNull();

    // The whole set is replaced, so what goes over the wire is the set the member ends up with.
    expect(sent.setRoles).toEqual([{ roleIds: [2] }]);
    // Read back from the server rather than trusting the optimistic UI. That the write reaches
    // the database is TeamMemberTests.Setting_a_members_roles_replaces_the_ones_they_held.
    await expect.poll(() => row("role.swap@test.local").textContent).toContain("Member");
    expect(row("role.swap@test.local")).not.toHaveTextContent("Viewer");
  });

  it("disables a member, then re-enables them", async () => {
    const { handlers, sent } = team(self(), account(43, "On Leave", "on.leave@test.local", [3]));
    const { user } = renderApp("/team/members", { handlers });

    await user.click(await screen.findByRole("row", { name: /on.leave@test.local/ }));
    const panel = within(drawer());
    await user.click(await panel.findByRole("button", { name: "Disable account" }));
    expect(await panel.findByRole("button", { name: "Re-enable account" })).toBeVisible();
    expect(sent.setDisabled).toEqual([{ disabled: true }]);
    // Read back, as above; TeamMemberTests.A_disabled_member_stays_disabled_until_re_enabled
    // holds the write itself.
    await expect.poll(() => row("on.leave@test.local").textContent).toContain("Disabled");

    // A disabled account keeps its roles and history but must not be able to sign in — that
    // refusal is the server's, and LoginTests.A_disabled_account_cannot_sign_in holds it.

    await user.click(panel.getByRole("button", { name: "Re-enable account" }));
    expect(await panel.findByRole("button", { name: "Disable account" })).toBeVisible();
    expect(sent.setDisabled).toEqual([{ disabled: true }, { disabled: false }]);
    await expect.poll(() => row("on.leave@test.local").textContent).toContain("Active");
  });

  it("won't let the last administrator be removed or disabled", async () => {
    const { handlers } = team(self(), account(44, "Someone Else", "someone@test.local", [3]));
    // The guard itself is the server's, held by
    // TeamGuardTests.The_last_administrator_cannot_lose_the_role_be_disabled_or_be_removed. This is
    // the refusal it sends, as CqApi renders an SWValidationException: a 400 carrying the code and
    // its message.
    const refusal = http.post(apiPath("/accounts/:id/setRoles"), () =>
      HttpResponse.json(
        {
          LAST_ADMINISTRATOR: [
            "This is the only member with the Administrator role. Give it to someone else first.",
          ],
        },
        { status: 400 },
      ),
    );
    const { user } = renderApp("/team/members", { handlers: [refusal, ...handlers] });

    await user.click(await screen.findByRole("row", { name: new RegExp(ADMIN.email) }));
    const panel = within(drawer());
    const role = await panel.findByRole("checkbox", { name: /^Administrator/ });

    // Nothing destructive is even offered on your own account.
    expect(panel.queryByRole("button", { name: "Remove from team" })).not.toBeInTheDocument();
    expect(panel.queryByRole("button", { name: "Disable account" })).not.toBeInTheDocument();

    // Dropping the role is offered, but the server refuses it — and says why.
    await user.click(role);
    await user.click(panel.getByRole("button", { name: "Save roles" }));
    expect(await panel.findByText(/only member with the Administrator role/i)).toBeVisible();
    // The unchecked box in the drawer is a rejected draft, not what the server holds.
    expect(row(ADMIN.email)).toHaveTextContent("Administrator");
  });

  it("filters and searches the member list", async () => {
    const { handlers } = team(self(), account(45, "Findable Person", "findable@test.local", [3]));
    const { user } = renderApp("/team/members", { handlers });

    const search = await screen.findByLabelText("Search members");
    await screen.findByRole("row", { name: /findable@test.local/ });
    await user.type(search, "Findable");
    expect(row("findable@test.local")).toBeVisible();
    expect(screen.queryByRole("row", { name: new RegExp(ADMIN.email) })).not.toBeInTheDocument();

    await user.clear(search);
    await user.click(screen.getByRole("button", { name: "Disabled" }));
    expect(screen.queryByRole("row", { name: /findable@test.local/ })).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Active" }));
    expect(row("findable@test.local")).toBeVisible();
  });
});
