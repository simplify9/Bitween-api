import { screen, waitFor } from "@testing-library/react";
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

const EDITOR = ["portal-properties.view", "portal-properties.edit"];

describe("a partner's own details, on our own Partner page", () => {
  it("shows what we show them, a secret without its value, and changes only what they may", async () => {
    const { handlers, saved } = backend();
    const { user } = renderApp("/portal/details", { as: { partner: ACME, permissions: EDITOR }, handlers });

    expect(await screen.findByRole("heading", { name: "Acme Freight" })).toBeVisible();
    expect(screen.getByDisplayValue("https://acme/in")).toBeEnabled();
    expect(screen.getByDisplayValue("A-1")).toBeDisabled();
    expect(screen.getByDisplayValue("accountNo")).toBeDisabled();
    expect(screen.getByLabelText("Value 2")).toHaveTextContent("••••••••");
    // None of ours: keys, how they sign in, adding or removing a property, locking one.
    expect(screen.queryByText("API keys")).not.toBeInTheDocument();
    expect(screen.queryByText("Login server identity")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Add property/ })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Mark apiKey secret" })).toBeDisabled();

    await user.clear(screen.getByDisplayValue("https://acme/in"));
    await user.type(screen.getByLabelText("Value 3"), "https://acme/v2");
    await user.click(screen.getByRole("button", { name: "Save changes" }));

    await waitFor(() => expect(saved).toEqual([{ values: { apiKey: "", webhookUrl: "https://acme/v2" } }]));
  });

  it("says a change waits for approval when their changes need it", async () => {
    const { handlers } = backend(true);
    const { user } = renderApp("/portal/details", { as: { partner: ACME, permissions: EDITOR }, handlers });

    await user.type(await screen.findByDisplayValue("https://acme/in"), "/v2");
    await user.click(screen.getByRole("button", { name: "Send for approval" }));
    expect(await screen.findByText(/waits for our approval/)).toBeVisible();
  });

  it("can't be changed by a role that may only look", async () => {
    renderApp("/portal/details", { as: { partner: ACME, permissions: ["portal-properties.view"] }, handlers: backend().handlers });
    expect(await screen.findByDisplayValue("https://acme/in")).toBeDisabled();
    expect(screen.queryByRole("button", { name: "Replace" })).not.toBeInTheDocument();
  });
});
