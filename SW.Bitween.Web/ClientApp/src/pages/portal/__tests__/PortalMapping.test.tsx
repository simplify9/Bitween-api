import { screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";
import { addFixedRule } from "../../../components/nativeMapper/__tests__/editorHarness";

const ACME = { id: 7, name: "Acme Freight" };
const EDITOR = ["portal-mappings.view", "portal-mappings.edit", "portal-exchange-files.view"];
const OURS = JSON.stringify({ version: 1, sourceFormat: "json", targetFormat: "json", fields: [], lists: [] });

/** The portal's mapping endpoints, recording what was saved and previewed. */
function portal(theirs: Record<string, string> | null = null) {
  const saved: unknown[] = [];
  const previews: unknown[] = [];
  const handlers = [
    http.get(apiPath("/portal/mapping"), () =>
      HttpResponse.json({
        subscriptionId: 3,
        subscriptionName: "Acme webhook",
        customizable: true,
        default: { MappingRules: OURS },
        theirs,
      }),
    ),
    http.get(apiPath("/portal/mappingsamples"), () =>
      HttpResponse.json([{ xchangeId: "x1", startedOn: "2026-11-24T10:00:00Z", inputName: "in.json" }]),
    ),
    http.get(apiPath("/portal/mappingsample"), () => HttpResponse.json({ data: '{"orderId":"A1"}' })),
    http.get(apiPath("/portal/propertykeys"), () => HttpResponse.json(["accountNo"])),
    http.post(apiPath("/portal/previewmapping"), async ({ request }) => {
      previews.push(await request.json());
      return HttpResponse.json({ outputDocument: "{}", contentType: "application/json", ruleErrors: [], error: null });
    }),
    http.post(apiPath("/portal/savemapping"), async ({ request }) => {
      saved.push(await request.json());
      return new HttpResponse(null, { status: 204 });
    }),
    http.post(apiPath("/portal/resetmapping"), () => new HttpResponse(null, { status: 204 })),
  ];
  return { handlers, saved, previews };
}

describe("a partner's own mapping", () => {
  it("is made from ours in the editor, with no global values, partner picker or AI", async () => {
    const { handlers, saved } = portal();
    const { user } = renderApp("/portal/mappings/3", { as: { partner: ACME, permissions: EDITOR }, handlers });

    expect(await screen.findByText("Our mapping")).toBeVisible();
    await user.click(screen.getByRole("button", { name: "Make your own version" }));
    await screen.findByRole("button", { name: "Save" }, { timeout: 5000 });

    await addFixedRule(user, "source", "acme");
    const choices = within(screen.getAllByRole("radiogroup", { name: "Where the value comes from" }).at(-1)!);
    expect(choices.queryByRole("radio", { name: "Global" })).not.toBeInTheDocument();
    expect(choices.getByRole("radio", { name: "Partner" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Generate/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("combobox", { name: /Preview as/i })).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Save" }));
    await waitFor(() => expect(saved).toHaveLength(1));
    const body = saved[0] as { subscriptionId: number; mapperProperties: Record<string, string> };
    expect(body.subscriptionId).toBe(3);
    expect(JSON.parse(body.mapperProperties.MappingRules).fields[0].target).toEqual(["source"]);
  });

  it("goes back to ours", async () => {
    const { handlers } = portal({ MappingRules: OURS });
    const { user } = renderApp("/portal/mappings/3", { as: { partner: ACME, permissions: EDITOR }, handlers });

    expect(await screen.findByText("Your own mapping")).toBeVisible();
    await user.click(screen.getByRole("button", { name: "Go back to ours" }));
  });

  it("is only seen by a role that can't change it", async () => {
    renderApp("/portal/mappings/3", { as: { partner: ACME, permissions: ["portal-mappings.view"] }, handlers: portal().handlers });
    expect(await screen.findByText("Your role can see this but not change it.")).toBeVisible();
    expect(screen.queryByRole("button", { name: "Make your own version" })).not.toBeInTheDocument();
  });
});
