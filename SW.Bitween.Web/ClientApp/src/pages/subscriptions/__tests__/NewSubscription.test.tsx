import { screen, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";

/** All subscriptions › New subscription: asks what starts it, and goes where that is created. */
const none = { result: [], totalCount: 0 };

describe("New subscription", () => {
  it("asks what starts it and goes where that kind is created", async () => {
    const { user, router } = renderApp("/subscriptions", {
      handlers: ["/subscriptions", "/documents", "/partners", "/apigateways", "/busgateways"].map((p) =>
        http.get(apiPath(p), () => HttpResponse.json(none)),
      ),
    });

    // Offered in the header, and in the empty list.
    await screen.findByText("No subscriptions yet", undefined, { timeout: 5000 });
    const buttons = screen.getAllByRole("button", { name: "New subscription" });
    expect(buttons).toHaveLength(2);
    await user.click(buttons[0]);

    const dialog = await screen.findByRole("dialog", { name: "What starts it?" });
    expect(within(dialog).getAllByRole("button").map((b) => b.querySelector("span span")?.textContent).filter(Boolean)).toEqual([
      "A partner calls Bitween",
      "On a schedule",
      "A message arrives on the bus",
      "Another subscription's response",
      "Rolling exchanges up",
    ]);
    await user.click(within(dialog).getByRole("button", { name: /On a schedule/ }));
    expect(router.state.location.pathname).toBe("/scheduled-jobs/new");
  });
});
