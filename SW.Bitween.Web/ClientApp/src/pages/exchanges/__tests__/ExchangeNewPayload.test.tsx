import { screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";

/** New exchange checks the payload against its information type's format before creating it. */
describe("a payload typed by hand", () => {
  it("is refused while it isn't the JSON its information type expects", async () => {
    const { user } = renderApp("/exchanges/new?target=informationType", {
      handlers: [
        http.get(apiPath("/documents"), () =>
          HttpResponse.json({ result: [{ id: 3, name: "Order", code: "ORDER", documentFormat: "Json" }], totalCount: 1 }),
        ),
        http.get(apiPath("/subscriptions"), () => HttpResponse.json({ result: [], totalCount: 0 })),
      ],
    });

    await user.click(await screen.findByRole("combobox", { name: /Information type/ }, { timeout: 5000 }));
    await user.click(await screen.findByRole("option", { name: /Order/ }));
    const payload = screen.getByRole("textbox", { name: /Payload/ });
    await user.click(payload);
    await user.paste('{"a": [');

    expect(await screen.findByText(/This isn't valid JSON/)).toBeVisible();
    expect(screen.getByRole("button", { name: "Create exchange" })).toBeDisabled();

    await user.clear(payload);
    await user.paste('{"a": [1]}');
    expect(screen.queryByText(/This isn't valid JSON/)).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Create exchange" })).toBeEnabled();
  });
});
