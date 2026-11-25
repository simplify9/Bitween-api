import { screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";

const ACME = { id: 7, name: "Acme Freight" };

function backend(needsApproval = false) {
  const saved: unknown[] = [];
  let waiting: { values: Record<string, string | null>; requestedOn: string } | null = null;
  const handlers = [
    http.get(apiPath("/portal/mappings"), () => HttpResponse.json([])),
    http.get(apiPath("/portal/properties"), () =>
      HttpResponse.json({
        properties: [
          { key: "accountNo", value: "A-1", secret: false, editable: false },
          { key: "apiKey", value: null, secret: true, editable: true },
          { key: "webhookUrl", value: "https://acme/in", secret: false, editable: true },
        ],
        needsApproval,
        waiting,
        rejected: null,
      }),
    ),
    http.post(apiPath("/portal/saveproperties"), async ({ request }) => {
      const body = (await request.json()) as { values: Record<string, string> };
      saved.push(body);
      if (needsApproval) waiting = { values: { webhookUrl: body.values.webhookUrl }, requestedOn: "2026-11-25T10:00:00Z" };
      return HttpResponse.json({ waiting: needsApproval });
    }),
  ];
  return { handlers, saved };
}

describe("a partner's own details in the portal", () => {
  it("shows what we show them, a secret without its value, and changes only what they may", async () => {
    const { handlers, saved } = backend();
    const { user } = renderApp("/portal/details", {
      as: { partner: ACME, permissions: ["portal-properties.view", "portal-properties.edit"] },
      handlers,
    });

    expect(await screen.findByText("https://acme/in")).toBeVisible();
    expect(screen.getByText("Secret")).toBeVisible();

    await user.click(screen.getByRole("button", { name: "Change" }));
    const dialog = screen.getByRole("dialog", { name: "Change your details" });
    expect(within(dialog).queryByLabelText("accountNo")).not.toBeInTheDocument();
    expect(within(dialog).getByLabelText(/apiKey/)).toHaveValue("");
    await user.clear(within(dialog).getByLabelText("webhookUrl"));
    await user.type(within(dialog).getByLabelText("webhookUrl"), "https://acme/v2");
    await user.click(within(dialog).getByRole("button", { name: "Save" }));

    await waitFor(() => expect(saved).toEqual([{ values: { apiKey: "", webhookUrl: "https://acme/v2" } }]));
  });

  it("says a change waits for approval when their changes need it", async () => {
    const { handlers } = backend(true);
    const { user } = renderApp("/portal/details", {
      as: { partner: ACME, permissions: ["portal-properties.view", "portal-properties.edit"] },
      handlers,
    });

    await user.click(await screen.findByRole("button", { name: "Change" }));
    await user.click(screen.getByRole("button", { name: "Send for approval" }));
    expect(await screen.findByText(/waits for our approval/)).toBeVisible();
  });

  it("can't be changed by a role that may only look", async () => {
    renderApp("/portal/details", { as: { partner: ACME, permissions: ["portal-properties.view"] }, handlers: backend().handlers });
    await screen.findByText("https://acme/in");
    expect(screen.queryByRole("button", { name: "Change" })).not.toBeInTheDocument();
  });
});
