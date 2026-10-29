import { screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { renderApp } from "../../../__tests__/support/renderApp";
import { empty, page, recorded } from "../../../__tests__/support/listPage";

describe("the scheduled retries list", () => {
  it("lists the retries waiting to run", async () => {
    const retries = recorded(
      "/delayedretries",
      page([
        {
          id: "r-1",
          on: "2026-10-10T12:00:00Z",
          subscriptionId: 7,
          subscriptionName: "Push to carrier",
          documentId: 2,
          documentName: "Order",
          exception: "Carrier timed out",
          startedOn: "2026-10-10T11:00:00Z",
          promotedProperties: null,
          retryPolicyId: null,
          retryPolicyName: null,
        },
      ]),
    );
    renderApp("/scheduled-retries", { handlers: [retries.handler, ...empty("/subscriptions", "/documents")] });

    expect(await screen.findByText("Push to carrier")).toBeVisible();
    expect(screen.getByText(/Carrier timed out/)).toBeVisible();
  });
});
