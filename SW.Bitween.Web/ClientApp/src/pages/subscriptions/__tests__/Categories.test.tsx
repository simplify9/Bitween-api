import { screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";
import { backend, sub } from "./subscriptionBackend";

globalThis.ResizeObserver ??= class {
  observe() {}
  unobserve() {}
  disconnect() {}
} as unknown as typeof ResizeObserver;

/** Subscription categories: filed under on the subscription, managed from All subscriptions. */
const CATEGORIES = [
  { id: 1, code: "FINANCE", description: "Invoices and payments", createdOn: "2026-10-01T00:00:00Z" },
  { id: 2, code: "LOGISTICS", description: null, createdOn: "2026-10-01T00:00:00Z" },
];
const categories = http.get(apiPath("/subscriptioncategories"), () => HttpResponse.json({ result: CATEGORIES, totalCount: 2 }));

describe("subscription categories", () => {
  it("files a subscription under the category picked", async () => {
    const { handlers, saves } = backend([sub({ categoryId: 1 })]);
    const { user } = renderApp("/subscriptions/10", { handlers: [categories, ...handlers] });

    const picker = await screen.findByRole("combobox", { name: "Category" }, { timeout: 5000 });
    await waitFor(() => expect(picker).toHaveValue("FINANCE"));
    await user.click(picker);
    await user.click(await screen.findByRole("option", { name: "LOGISTICS" }));
    await user.click(screen.getByRole("button", { name: "Save changes" }));

    await waitFor(() => expect(saves).toHaveLength(1));
    expect(saves[0].categoryId).toBe(2);
  });

  it("creates one, and won't delete one that subscriptions are filed under", async () => {
    const created: unknown[] = [];
    const { handlers } = backend([sub({ categoryId: 1 })]);
    const { user } = renderApp("/subscriptions", {
      handlers: [
        categories,
        http.post(apiPath("/subscriptioncategories"), async ({ request }) => {
          created.push(await request.json());
          return HttpResponse.json(3);
        }),
        http.get(apiPath("/subscriptions"), () => HttpResponse.json({ result: [sub({ categoryId: 1 })], totalCount: 1 })),
        ...handlers,
      ],
    });

    await user.click(await screen.findByRole("button", { name: "Categories" }, { timeout: 5000 }));
    const dialog = await screen.findByRole("dialog");
    expect(await within(dialog).findByText("1 subscription")).toBeVisible();
    expect(within(dialog).getByRole("button", { name: "Delete FINANCE" })).toBeDisabled();
    expect(within(dialog).getByRole("button", { name: "Delete LOGISTICS" })).toBeEnabled();

    await user.click(within(dialog).getByRole("button", { name: "New category" }));
    await user.type(await screen.findByLabelText("Code"), "CUSTOMS");
    await user.click(screen.getByRole("button", { name: "Create" }));
    await waitFor(() => expect(created).toEqual([{ code: "CUSTOMS", description: "" }]));
  });
});
