import { screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";

/**
 * Reads are permission-guarded too, which is easy to get wrong in the other direction: a page can
 * legitimately need data from an area the viewer has no business browsing. That the server refuses
 * the read is ReadGuardTests.One_view_permission_reads_its_own_list_and_no_other; this is the page
 * that has to survive the refusal.
 */
const DOCS_ONLY = {
  id: 77,
  email: "no.subscriptions@test.local",
  name: "No Subscriptions",
  roles: [{ id: 12, name: "No Subscriptions" }],
  permissions: ["documents.view"],
};

/** An information type as the list returns it (DocumentRow). */
const PURCHASE_ORDER = {
  id: 1,
  code: "PURCHASE_ORDER",
  name: "Purchase order",
  documentFormat: "Json",
  busEnabled: false,
  busMessageTypeName: null,
  duplicateInterval: 0,
  disregardsUnfilteredMessages: false,
  promotedProperties: [],
  usedByCount: 1,
  retiredOn: null,
};

describe("a page reading another area's list", () => {
  it("still loads when the area behind its Used by count is refused", async () => {
    let refusals = 0;
    renderApp("/information-types", {
      as: DOCS_ONLY,
      handlers: [
        http.get(apiPath("/documents"), () => HttpResponse.json({ result: [PURCHASE_ORDER], totalCount: 1 })),
        // EnsurePermission throws SWUnauthorizedException, which CqApi answers with a bare 401 —
        // the same one a missing token gets. So the client refreshes and asks again, and is
        // refused again.
        http.get(apiPath("/subscriptions"), () => {
          refusals++;
          return HttpResponse.json(
            { type: "https://tools.ietf.org/html/rfc9110#section-15.5.2", title: "Unauthorized", status: 401 },
            { status: 401, headers: { "Content-Type": "application/problem+json" } },
          );
        }),
        http.post(apiPath("/accounts/login"), () => HttpResponse.json({ jwt: "refreshed-token", mustChangePassword: false })),
      ],
    });

    // The information types list counts how many subscriptions use each type, which needs the
    // subscriptions list this role can't read. The count is what's expendable, not the page.
    expect(await screen.findByRole("table")).toBeVisible();
    expect(screen.getByText("Purchase order")).toBeVisible();
    await expect.poll(() => refusals).toBe(2);
    expect(screen.queryByText("You don't have access to this page")).not.toBeInTheDocument();
    expect(screen.queryAllByText(/failed|error/i)).toHaveLength(0);
  });
});
