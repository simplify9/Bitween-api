import { screen, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { ADMIN, apiPath, renderApp } from "../../../__tests__/support/renderApp";

/**
 * Adding a member with an invite, and following one up. Whether the invite is emailed or handed
 * over depends on the instance having a mail server, which the config endpoint says.
 */
const ROLES = [{ id: 2, name: "Member", description: "Runs and configures integrations.", isSystem: true, permissions: [], memberCount: 0, createdOn: "2026-01-01T00:00:00Z" }];

type Invite = { expiresOn: string; emailedOn: string | null; emailError: string | null } | null;

const account = (id: number, name: string, email: string, hasPassword = true, invite: Invite = null) => ({
  id,
  name,
  email,
  role: "Member",
  disabled: false,
  lockoutEnd: null,
  createdOn: "2026-01-01T00:00:00Z",
  roles: [{ id: 2, name: "Member" }],
  hasPassword,
  invite,
});

const inTwoDays = () => new Date(Date.now() + 2 * 864e5).toISOString();
const twoDaysAgo = () => new Date(Date.now() - 2 * 864e5).toISOString();

/** The server's members, which a create adds to, and what each write carried. */
function team(invite: (id: number) => object, ...accounts: ReturnType<typeof account>[]) {
  const sent: { created: Record<string, unknown>[]; invited: string[] } = { created: [], invited: [] };
  const handlers = [
    http.get(apiPath("/accounts"), () => HttpResponse.json({ result: accounts, totalCount: accounts.length })),
    http.get(apiPath("/roles"), () => HttpResponse.json({ result: ROLES, totalCount: ROLES.length })),
    http.get(apiPath("/audit"), () => HttpResponse.json({ result: [], totalCount: 0 })),
    http.post(apiPath("/accounts"), async ({ request }) => {
      const body = (await request.json()) as Record<string, unknown>;
      sent.created.push(body);
      accounts.push(account(77, String(body.name), String(body.email), !body.invite));
      return HttpResponse.json(77);
    }),
    http.post(apiPath("/accounts/:id/invite"), ({ params }) => {
      sent.invited.push(String(params.id));
      const found = accounts.find((a) => a.id === Number(params.id))!;
      found.invite = { expiresOn: inTwoDays(), emailedOn: null, emailError: null };
      return HttpResponse.json(invite(Number(params.id)));
    }),
  ];
  return { handlers, sent };
}

const self = () => account(ADMIN.id, ADMIN.name, ADMIN.email);

async function addJane(user: ReturnType<typeof renderApp>["user"]) {
  const dialog = within(await screen.findByRole("dialog", { name: "Add a member" }));
  await user.type(dialog.getByRole("textbox", { name: "Name" }), "Jane Cooper");
  await user.type(dialog.getByRole("textbox", { name: "Email" }), "jane@acme.test");
  return dialog;
}

describe("inviting a member", () => {
  it("emails the invite when there is a mail server, and lists them as invited", async () => {
    const { handlers, sent } = team(() => ({ emailed: true, emailError: null, expiresOn: inTwoDays(), link: null }), self());
    const { user } = renderApp("/team/members?add=1", { handlers, config: { mailServerConfigured: true } });

    const dialog = await addJane(user);
    // An invite is the default: nobody has to make up a password for someone else.
    expect(dialog.getByRole("radio", { name: /Send an invite/ })).toBeChecked();
    expect(dialog.queryByLabelText("Password")).not.toBeInTheDocument();
    await user.click(dialog.getByRole("button", { name: "Add and email invite" }));

    await expect.poll(() => screen.queryByRole("dialog", { name: "Add a member" })).toBeNull();
    expect(sent.created[0]).toMatchObject({ name: "Jane Cooper", email: "jane@acme.test", invite: true });
    expect(sent.created[0]).not.toHaveProperty("password");
    expect(sent.invited).toEqual(["77"]);
    expect(await screen.findByRole("row", { name: /jane@acme.test/ })).toHaveTextContent("Invited");
  });

  it("hands the link over once when there is no mail server to send it", async () => {
    const link = "https://bitween.acme.test/set-password?token=t0k3n";
    const { handlers } = team(() => ({ emailed: false, emailError: null, expiresOn: inTwoDays(), link }), self());
    const { user } = renderApp("/team/members?add=1", { handlers });

    expect(await screen.findByText(/Bitween can't email members yet/)).toBeVisible();
    const dialog = await addJane(user);
    await user.click(dialog.getByRole("button", { name: "Add and make invite link" }));

    const handOver = within(await screen.findByRole("dialog", { name: "Jane Cooper is added" }));
    expect(handOver.getByText(link)).toBeVisible();
    expect(handOver.getByText(/Bitween keeps no copy/)).toBeVisible();
  });

  it("can still set a password instead", async () => {
    const { handlers, sent } = team(() => ({}), self());
    const { user } = renderApp("/team/members?add=1", { handlers, config: { mailServerConfigured: true } });

    const dialog = await addJane(user);
    await user.click(dialog.getByRole("radio", { name: /Set a password now/ }));
    await user.type(dialog.getByLabelText("Password"), "Set-for-Jane-1");
    await user.click(dialog.getByRole("button", { name: "Add member" }));

    await expect.poll(() => screen.queryByRole("dialog", { name: "Add a member" })).toBeNull();
    expect(sent.created[0]).toMatchObject({ password: "Set-for-Jane-1" });
    expect(sent.created[0]).not.toHaveProperty("invite");
    expect(sent.invited).toEqual([]);
  });

  it("asks for no password when everyone signs in with Microsoft", async () => {
    const { handlers, sent } = team(() => ({ emailed: true, emailError: null, expiresOn: null, link: null }), self());
    const { user } = renderApp("/team/members?add=1", {
      handlers,
      config: { mailServerConfigured: true, disableEmailPasswordLogin: true },
    });

    const dialog = await addJane(user);
    expect(dialog.queryByRole("radio")).not.toBeInTheDocument();
    expect(dialog.getByRole("checkbox", { name: /Email them that they've been added/ })).toBeChecked();
    await user.click(dialog.getByRole("button", { name: "Add member" }));

    await expect.poll(() => sent.invited).toEqual(["77"]);
    expect(sent.created[0]).not.toHaveProperty("password");
  });
});

describe("a member's invite", () => {
  it("that expired is shown as such, and can be sent again", async () => {
    const late = account(50, "Late Joiner", "late@acme.test", false, { expiresOn: twoDaysAgo(), emailedOn: twoDaysAgo(), emailError: null });
    const { handlers, sent } = team(() => ({ emailed: true, emailError: null, expiresOn: inTwoDays(), link: null }), self(), late);
    const { user } = renderApp("/team/members", { handlers, config: { mailServerConfigured: true } });

    const row = await screen.findByRole("row", { name: /late@acme.test/ });
    expect(row).toHaveTextContent("Invite expired");
    await user.click(row);

    const drawer = within(await screen.findByRole("dialog", { name: "Member details" }));
    expect(drawer.getByText(/Their invite expired/)).toBeVisible();
    await user.click(drawer.getByRole("button", { name: "Resend invite" }));

    expect(await drawer.findByText("Sent to late@acme.test")).toBeVisible();
    expect(sent.invited).toEqual(["50"]);
  });

  it("whose email failed says why", async () => {
    const stuck = account(51, "Stuck", "stuck@acme.test", false, { expiresOn: inTwoDays(), emailedOn: null, emailError: "Connection refused" });
    const { handlers } = team(() => ({}), self(), stuck);
    renderApp("/team/members/51", { handlers, config: { mailServerConfigured: true } });

    const drawer = within(await screen.findByRole("dialog", { name: "Member details" }));
    expect(await drawer.findByText(/The invite email couldn't be sent: Connection refused/)).toBeVisible();
  });
});
