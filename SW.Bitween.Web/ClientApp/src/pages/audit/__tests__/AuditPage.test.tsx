import { act, screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";
import { auditRow, auditTrail } from "./trail";

/**
 * The trail page. Which rows a filter returns is `Audit/Search.cs`'s business; what's left here
 * is that each filter reaches the query, and what the page does with the answer.
 */
const members = http.get(apiPath("/accounts"), () => HttpResponse.json({ result: [], totalCount: 0 }));

/** One save that touched two rows — a partner and its key — beside an unrelated one. */
const ROWS = [
  auditRow({ entityName: "Partner", entityKey: "42", correlationId: "save-a", sequence: 0 }),
  auditRow({ entityName: "ApiCredential", entityKey: "42:primary", correlationId: "save-a", sequence: 1 }),
  auditRow({ entityName: "WorkGroup", entityKey: "7", correlationId: "save-b" }),
];

describe("the audit trail page", () => {
  it("filters, groups one save, and clears", async () => {
    const trail = auditTrail(ROWS);
    const { user, router } = renderApp("/audit", { handlers: [trail.handler, members] });

    expect(await screen.findByRole("heading", { name: "Audit trail" })).toBeVisible();
    expect(await screen.findByText("WorkGroup")).toBeVisible();

    // Narrowing to this one row proves the entity filters reach the query, not just the URL.
    await act(() => router.navigate("/audit?entityName=Partner&entityKey=42"));
    await waitFor(() => expect(screen.queryByText("WorkGroup", { selector: "td *" })).not.toBeInTheDocument());
    expect(trail.asked.at(-1)?.get("entityName")).toBe("Partner");
    expect(trail.asked.at(-1)?.get("entityKey")).toBe("42");
    expect(screen.getByRole("row", { name: /Partner/ })).toBeVisible();
    expect(screen.getByRole("link", { name: "42" })).toHaveAttribute("href", "/partners/42");

    // "Same save" pivots to the correlation id — every row one SaveChanges wrote.
    await user.click(screen.getAllByRole("button", { name: "Same save" })[0]);
    expect(router.state.location.search).toMatch(/correlationId=save-a/);
    expect(await screen.findByText("Showing one save only")).toBeVisible();
    expect(trail.asked.at(-1)?.get("correlationId")).toBe("save-a");

    await user.click(screen.getByRole("button", { name: "Clear filters" }));
    expect(router.state.location.pathname).toBe("/audit");
    expect(router.state.location.search).toBe("");
  });

  // Number("bad") is NaN, which used to go out on the wire as offset=NaN.
  it.each(["bad", "-5"])("doesn't break on a junk offset of %s in the URL", async (offset) => {
    const trail = auditTrail(ROWS);
    renderApp(`/audit?offset=${offset}`, { handlers: [trail.handler, members] });

    expect(await screen.findByRole("heading", { name: "Audit trail" })).toBeVisible();
    expect(await screen.findByText(/Showing 1[–-]/)).toBeVisible();
    expect(trail.asked.map((q) => q.get("offset"))).toEqual(["0"]);
  });
});
