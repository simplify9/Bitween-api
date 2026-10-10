import { screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { ALL_PERMISSIONS, apiPath, renderApp } from "../../../__tests__/support/renderApp";
import { empty, page } from "../../../__tests__/support/listPage";

const acme = { id: 3, name: "Acme Retail", subscriptionsCount: 0, keys: 1, propertyKeys: [] };

/** What the palette reads, plus the Global values list it is opened over. */
const handlers = [
  http.get(apiPath("/partners"), () => HttpResponse.json(page([acme]))),
  ...empty(
    "/subscriptions",
    "/documents",
    "/apigateways",
    "/busgateways",
    "/datasources",
    "/globaladaptervaluessets",
    "/retrypolicies",
    "/workgroups",
  ),
];

describe("the command palette", () => {
  it("opens on Ctrl+K and goes to a page found by one of its words", async () => {
    const { user, router } = renderApp("/global-values", { handlers });
    await screen.findByRole("heading", { name: /Global values/ });

    await user.keyboard("{Control>}k{/Control}");
    const palette = within(screen.getByRole("dialog", { name: "Search Bitween" }));
    await user.type(palette.getByRole("combobox"), "api keys");
    await user.click(palette.getByRole("option", { name: /^Partners/ }));

    await waitFor(() => expect(router.state.location.pathname).toBe("/partners"));
    expect(screen.queryByRole("dialog", { name: "Search Bitween" })).not.toBeInTheDocument();
  });

  it("opens a partner by name", async () => {
    const { user, router } = renderApp("/global-values", {
      handlers: [
        // The partner page it lands on: only the arrival matters here.
        http.get(apiPath("/partners/3"), () => new HttpResponse(null, { status: 404 })),
        ...empty("/xchanges"),
        ...handlers,
      ],
    });
    await screen.findByRole("heading", { name: /Global values/ });

    await user.click(screen.getByRole("button", { name: /Search.*(Ctrl K|⌘K)/ }));
    const palette = within(screen.getByRole("dialog", { name: "Search Bitween" }));
    await user.type(palette.getByRole("combobox"), "acme");
    await user.keyboard("{ArrowDown}{Enter}");

    await waitFor(() => expect(router.state.location.pathname).toBe("/partners/3"));
  });

  it("offers nothing the session may not open", async () => {
    const { user } = renderApp("/global-values", {
      handlers,
      as: { permissions: ALL_PERMISSIONS.filter((p) => p !== "partners.view") },
    });
    await screen.findByRole("heading", { name: /Global values/ });

    await user.keyboard("{Control>}k{/Control}");
    const palette = within(screen.getByRole("dialog", { name: "Search Bitween" }));
    await user.type(palette.getByRole("combobox"), "acme partner");
    expect(await palette.findByText(/Nothing matches/)).toBeVisible();
  });
});
