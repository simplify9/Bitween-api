import { screen, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";

const ROLES = [
  { id: 1, name: "Administrator", description: "Everything." },
  { id: 2, name: "Member", description: "Runs and configures integrations." },
].map((r) => ({ ...r, isSystem: true, permissions: [], memberCount: 0, createdOn: "2026-01-01T00:00:00Z" }));

const inDays = (days: number) => new Date(Date.now() + days * 24 * 3600 * 1000).toISOString();

const token = (id: number, name: string, status: "active" | "expired" | "revoked", expiresInDays = 60) => ({
  id,
  name,
  shown: `bwt_abcd000${id}`,
  createdOn: "2026-09-01T00:00:00Z",
  createdBy: "9999",
  expiresOn: inDays(expiresInDays),
  revokedOn: status === "revoked" ? "2026-10-01T00:00:00Z" : null,
  lastUsedOn: null,
  lastUsedFrom: null,
  status,
});

/** Held like the server holds them, so each write is read back through the list. */
function serviceAccounts() {
  const accounts = [
    {
      id: 7,
      name: "lab-apply",
      disabled: false,
      createdOn: "2026-09-01T00:00:00Z",
      lastSignInOn: null as string | null,
      roles: [{ id: 2, name: "Member" }],
      tokens: [token(1, "lab job", "active", 5)],
    },
  ];
  const sent: Record<string, unknown[]> = { create: [], issue: [], revoke: [] };
  const handlers = [
    http.get(apiPath("/serviceaccounts"), () => HttpResponse.json(accounts)),
    http.get(apiPath("/roles"), () => HttpResponse.json({ result: ROLES, totalCount: ROLES.length })),
    http.get(apiPath("/audit"), () => HttpResponse.json({ result: [], totalCount: 0 })),
    http.post(apiPath("/serviceaccounts"), async ({ request }) => {
      const body = (await request.json()) as { name: string; roleIds: number[] };
      sent.create.push(body);
      accounts.push({ ...accounts[0], id: 8, name: body.name, roles: [], tokens: [] });
      return HttpResponse.json(8);
    }),
    http.post(apiPath("/serviceaccounts/:id/issueToken"), async ({ request }) => {
      const body = (await request.json()) as { name: string; expiresInDays: number };
      sent.issue.push(body);
      const issued = token(2, body.name, "active", body.expiresInDays);
      accounts[0].tokens.push(issued);
      return HttpResponse.json({ ...issued, token: "bwt_THEWHOLETOKENVALUEisHEREonlyONCE01234567" });
    }),
    http.post(apiPath("/serviceaccounts/:id/revokeToken"), async ({ request }) => {
      const body = (await request.json()) as { tokenId: number };
      sent.revoke.push(body);
      const t = accounts[0].tokens.find((x) => x.id === body.tokenId)!;
      Object.assign(t, { status: "revoked", revokedOn: "2026-10-11T00:00:00Z" });
      return new HttpResponse(null, { status: 204 });
    }),
  ];
  return { handlers, sent };
}

const openDrawer = async () => within(await screen.findByRole("dialog", { name: "Service account details" }));

describe("service accounts", () => {
  it("lists each account with its active tokens", async () => {
    const { handlers } = serviceAccounts();
    renderApp("/team/service-accounts", { handlers });

    const row = await screen.findByRole("row", { name: /lab-apply/ });
    expect(row).toHaveTextContent("Member");
    expect(row).toHaveTextContent("1 active");
  });

  it("creates one and opens it", async () => {
    const { handlers, sent } = serviceAccounts();
    const { user } = renderApp("/team/service-accounts", { handlers });

    await user.click((await screen.findAllByRole("button", { name: /New service account/ }))[0]);
    await user.type(screen.getByLabelText("Name"), "ci-publish");
    await user.click(screen.getByRole("checkbox", { name: /^Member/ }));
    await user.click(screen.getByRole("button", { name: "Create" }));

    expect(sent.create).toEqual([{ name: "ci-publish", roleIds: [2] }]);
    expect(await (await openDrawer()).findByRole("heading", { name: "ci-publish" })).toBeInTheDocument();
  });

  it("shows a token once when it's issued, and warns of one about to expire", async () => {
    const { handlers, sent } = serviceAccounts();
    const { user } = renderApp("/team/service-accounts/7", { handlers });
    const drawer = await openDrawer();

    expect(await drawer.findByText(/Expires in 5d/)).toBeInTheDocument();

    await user.type(drawer.getByLabelText("New token"), "replacement");
    await user.selectOptions(drawer.getByLabelText("Expires after"), "30");
    await user.click(drawer.getByRole("button", { name: /Issue token/ }));

    expect(sent.issue).toEqual([{ name: "replacement", expiresInDays: 30 }]);
    expect(await drawer.findByText("bwt_THEWHOLETOKENVALUEisHEREonlyONCE01234567")).toBeInTheDocument();
    // Two active is the most it can hold, so there's nothing more to issue.
    expect(drawer.getByText(/the most it can hold/)).toBeInTheDocument();

    await user.click(drawer.getByRole("button", { name: "Done" }));
    expect(drawer.queryByText("bwt_THEWHOLETOKENVALUEisHEREonlyONCE01234567")).toBeNull();
  });

  it("revokes a token after asking", async () => {
    const { handlers, sent } = serviceAccounts();
    const { user } = renderApp("/team/service-accounts/7", { handlers });
    const drawer = await openDrawer();

    await user.click(await drawer.findByRole("button", { name: "Revoke" }));
    await user.click(screen.getByRole("button", { name: "Revoke token" }));

    expect(sent.revoke).toEqual([{ tokenId: 1 }]);
    expect(await drawer.findByText("Revoked")).toBeInTheDocument();
  });
});
