import { screen, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";
import { auditRow, auditTrail } from "../../audit/__tests__/trail";

/**
 * The members and roles pages carry their own history, like every other entity.
 *
 * GET /roles and /roles/{id}: `RawRole` in src/api/http/team.ts.
 */
const VIEWER = {
  id: 3,
  name: "Viewer",
  description: "Sees everything, changes nothing.",
  isSystem: true,
  permissions: ["partners.view"],
  memberCount: 1,
  createdOn: "2026-01-01T00:00:00Z",
};
const AUDITORS = {
  id: 12,
  name: "Auditors",
  description: "Partners, read-only.",
  isSystem: false,
  permissions: ["partners.view"],
  memberCount: 0,
  createdOn: "2026-09-01T00:00:00Z",
};

const roles = http.get(apiPath("/roles"), () => HttpResponse.json({ result: [VIEWER, AUDITORS], totalCount: 2 }));
const catalog = http.get(apiPath("/permissions"), () =>
  HttpResponse.json([
    {
      id: "partners",
      label: "Partners",
      group: "Configuration",
      description: "",
      actions: [{ id: "view", description: "See partners." }],
    },
  ]),
);

describe("team history", () => {
  it("a custom role's page carries its history", async () => {
    // Built-in roles are deliberately excluded — their grants are computed rather than stored,
    // so nothing ever edits one — which makes a custom role the case worth covering.
    const trail = auditTrail([auditRow({ entityName: "Role", entityKey: "12" })]);
    const { user } = renderApp("/team/roles", {
      handlers: [
        roles,
        catalog,
        http.get(apiPath("/roles/12"), () => HttpResponse.json(AUDITORS)),
        trail.handler,
      ],
    });

    await user.click(await screen.findByRole("link", { name: /Auditors/ }));
    const panel = (await screen.findByRole("heading", { name: "History", level: 2 })).closest("section")!;
    expect(await within(panel).findByRole("row", { name: /Added/ })).toBeVisible();
    expect(trail.asked.at(-1)?.get("entityName")).toBe("Role");
    expect(trail.asked.at(-1)?.get("entityKey")).toBe("12");
  });

  it("a member drawer shows that member's history", async () => {
    const trail = auditTrail([auditRow({ entityName: "Account", entityKey: "7" })]);
    const { user } = renderApp("/team/members", {
      handlers: [
        // `RawAccount` in src/api/http/team.ts.
        http.get(apiPath("/accounts"), () =>
          HttpResponse.json({
            result: [
              {
                id: 7,
                name: "Playwright Drawer",
                email: "drawer@test.local",
                role: "Member",
                disabled: false,
                lockoutEnd: null,
                createdOn: "2026-09-01T00:00:00Z",
                roles: [{ id: 3, name: "Viewer" }],
              },
            ],
            totalCount: 1,
          }),
        ),
        roles,
        trail.handler,
      ],
    });

    await user.click(await screen.findByRole("row", { name: /drawer@test\.local/ }));
    const drawer = await screen.findByRole("dialog", { name: "Member details" });

    expect(await within(drawer).findByRole("heading", { name: "History" })).toBeVisible();
    expect(await within(drawer).findByRole("row", { name: /Added/ })).toBeVisible();
    expect(trail.asked.at(-1)?.get("entityName")).toBe("Account");
    expect(trail.asked.at(-1)?.get("entityKey")).toBe("7");
    // The other question worth asking about a person: what they changed, not what was done to them.
    expect(within(drawer).getByRole("link", { name: "What this member changed" })).toHaveAttribute(
      "href",
      "/audit?userId=7",
    );
  });
});
