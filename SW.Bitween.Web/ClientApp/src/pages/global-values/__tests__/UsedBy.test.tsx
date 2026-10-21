import { screen, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";
import { page } from "../../../__tests__/support/listPage";

const spaces = { id: "spaces", name: "Spaces", values: { url: "https://nyc3.example.test" }, secretProperties: [] };
const unused = { id: "unused", name: "Unused", values: { url: "https://old.example.test" }, secretProperties: [] };

/**
 * A subscription list row as the server sends it: the adapter properties withheld, and what they
 * referenced named instead. The set id is in a different case from the set's, as a token may be.
 */
const listRow = {
  id: 68,
  name: "VFS report",
  type: "Aggregation",
  documentId: 1,
  partnerId: null,
  handlerProperties: [],
  mapperProperties: [],
  receiverProperties: [],
  validatorProperties: [],
  references: { globals: [{ setId: "Spaces", keys: ["url", "secretKey"] }], partnerProperties: [] },
};

const subscriptions = http.get(apiPath("/subscriptions"), () => HttpResponse.json(page([listRow])));

describe("global values used by", () => {
  it("names the subscriptions that reference a set from what the list says they reference", async () => {
    renderApp("/global-values", {
      handlers: [http.get(apiPath("/globaladaptervaluessets"), () => HttpResponse.json(page([spaces, unused]))), subscriptions],
    });

    const used = await screen.findByRole("row", { name: /Spaces/ });
    expect(await within(used).findByRole("link", { name: "VFS report" })).toBeVisible();
    expect(within(screen.getByRole("row", { name: /Unused/ })).queryByRole("link", { name: "VFS report" })).toBeNull();
  });

  it("lists them on the set's page instead of calling it safe to delete", async () => {
    renderApp("/global-values/spaces", {
      handlers: [
        http.get(apiPath("/globaladaptervaluessets/spaces"), () => HttpResponse.json(spaces)),
        subscriptions,
        http.get(apiPath("/audit"), () => HttpResponse.json(page([]))),
      ],
    });

    // Twice: in the set's "Used by" panel, and beside the url value it reads.
    expect(await screen.findAllByRole("link", { name: /VFS report/ })).toHaveLength(2);
    expect(screen.queryByText(/safe to delete/)).toBeNull();
  });
});
