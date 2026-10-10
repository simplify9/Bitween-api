import { screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";

describe("creating an API gateway", () => {
  it("shows a URL name the server refuses under the URL name", async () => {
    const message = "'orders' is already the address of the gateway 'Orders inbound'.";
    const { user } = renderApp("/api-gateways/new", {
      handlers: [
        http.post(apiPath("/apigateways"), () =>
          HttpResponse.json({ GATEWAY_URL_NAME_TAKEN: [message] }, { status: 400 }),
        ),
      ],
    });

    await user.type(await screen.findByRole("textbox", { name: "Name" }), "Orders");
    await user.click(screen.getByRole("button", { name: /Create/ }));

    const url = screen.getByRole("textbox", { name: "URL name" });
    expect(await screen.findByText(message)).toBeVisible();
    expect(url).toBeInvalid();
    expect(url).toHaveAccessibleDescription(message);
  });
});
