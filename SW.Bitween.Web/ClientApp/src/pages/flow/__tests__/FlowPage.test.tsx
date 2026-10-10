import { screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { renderApp } from "../../../__tests__/support/renderApp";
import { empty } from "../../../__tests__/support/listPage";

describe("the flow page", () => {
  it("opens on an empty instance without asking for anything it can't have", async () => {
    renderApp("/flow", { handlers: [...empty("/subscriptions", "/documents", "/apigateways", "/busgateways")] });
    expect(await screen.findByRole("heading", { level: 1 })).toBeVisible();
  });
});
