import { screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";
import { addFixedRule } from "../../../components/nativeMapper/__tests__/editorHarness";

const ACME = { id: 7, name: "Acme Freight" };
const EDITOR = ["portal-mappings.view", "portal-mappings.edit", "portal-exchange-files.view"];
const OURS = JSON.stringify({ version: 1, sourceFormat: "json", targetFormat: "json", fields: [], lists: [] });

/** The portal's mapping endpoints, recording what was saved and previewed. */
function portal(
  initially: Record<string, string> | null = null,
  saveFails = false,
  approval: {
    needsApproval?: boolean;
    waiting?: { theirs: Record<string, string> | null; requestedOn: string } | null;
    rejected?: { note: string | null; decidedOn: string } | null;
  } = {},
) {
  const saved: unknown[] = [];
  const previews: unknown[] = [];
  const resets: unknown[] = [];
  const withdrawn: unknown[] = [];
  let theirs = initially;
  let waiting = approval.waiting ?? null;
  const handlers = [
    http.get(apiPath("/portal/mapping"), () =>
      HttpResponse.json({
        subscriptionId: 3,
        subscriptionName: "Acme webhook",
        customizable: true,
        default: { MappingRules: OURS },
        theirs,
        setAside: false,
        needsApproval: approval.needsApproval ?? false,
        waiting,
        rejected: approval.rejected ?? null,
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
      const body = (await request.json()) as { mapperProperties: Record<string, string> };
      saved.push(body);
      if (saveFails)
        return HttpResponse.json({ GLOBAL_VALUES_NOT_ALLOWED: ["Your mapping can't read these internal values: company.apiKey."] }, { status: 400 });
      if (approval.needsApproval) waiting = { theirs: body.mapperProperties, requestedOn: "2026-11-25T09:00:00Z" };
      else theirs = body.mapperProperties;
      return HttpResponse.json({ waiting: approval.needsApproval ?? false });
    }),
    http.post(apiPath("/portal/withdrawmapping"), async ({ request }) => {
      withdrawn.push(await request.json());
      waiting = null;
      return new HttpResponse(null, { status: 204 });
    }),
    http.post(apiPath("/portal/resetmapping"), async ({ request }) => {
      resets.push(await request.json());
      theirs = null;
      return new HttpResponse(null, { status: 204 });
    }),
  ];
  return { handlers, saved, previews, resets, withdrawn };
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
    const { handlers, resets } = portal({ MappingRules: OURS });
    const { user } = renderApp("/portal/mappings/3", { as: { partner: ACME, permissions: EDITOR }, handlers });

    expect(await screen.findByText("Your own mapping")).toBeVisible();
    await user.click(screen.getByRole("button", { name: "Go back to ours" }));
    expect(await screen.findByText("Our mapping")).toBeVisible();
    expect(resets).toEqual([{ subscriptionId: 3 }]);
  });

  it("says so in the editor when a save is refused, and keeps the changes unsaved", async () => {
    const { handlers } = portal(null, true);
    const { user } = renderApp("/portal/mappings/3", { as: { partner: ACME, permissions: EDITOR }, handlers });

    await user.click(await screen.findByRole("button", { name: "Make your own version" }));
    await screen.findByRole("button", { name: "Save" }, { timeout: 5000 });
    await addFixedRule(user, "source", "acme");
    await user.click(screen.getByRole("button", { name: "Save" }));

    expect(await screen.findByText(/can't read these internal values/)).toBeVisible();
    expect(screen.getByRole("button", { name: "Save" })).toBeEnabled();
  });

  it("suggests their own partner property names", async () => {
    const { handlers } = portal();
    const { user } = renderApp("/portal/mappings/3", { as: { partner: ACME, permissions: EDITOR }, handlers });

    await user.click(await screen.findByRole("button", { name: "Make your own version" }));
    await screen.findByRole("button", { name: "Save" }, { timeout: 5000 });
    await addFixedRule(user, "account", "x");
    await user.click(screen.getAllByRole("radio", { name: "Partner" }).at(-1)!);
    const key = screen.getAllByLabelText("Partner property key").at(-1)!;
    // The box lists its suggestions while it has focus.
    await user.click(key);
    await waitFor(() => expect(document.querySelector(`[id="${key.getAttribute("list")}"] option[value="accountNo"]`)).not.toBeNull());
  });

  it("is only seen by a role that can't change it", async () => {
    renderApp("/portal/mappings/3", { as: { partner: ACME, permissions: ["portal-mappings.view"] }, handlers: portal().handlers });
    expect(await screen.findByText("Your role can see this but not change it.")).toBeVisible();
    expect(screen.queryByRole("button", { name: "Make your own version" })).not.toBeInTheDocument();
  });

  it("waits for approval when their changes need it, and says so", async () => {
    const { handlers, saved } = portal(null, false, { needsApproval: true });
    const { user } = renderApp("/portal/mappings/3", { as: { partner: ACME, permissions: EDITOR }, handlers });

    expect(await screen.findByText(/Your changes wait for our approval/)).toBeVisible();
    await user.click(screen.getByRole("button", { name: "Make your own version" }));
    await screen.findByRole("button", { name: "Save" }, { timeout: 5000 });
    await addFixedRule(user, "source", "acme");
    await user.click(screen.getByRole("button", { name: "Save" }));
    await waitFor(() => expect(saved).toHaveLength(1));
    await user.click(screen.getByRole("button", { name: "Back" }));

    expect(await screen.findByText(/waits for our approval\. You keep getting ours until then\./)).toBeVisible();
    expect(screen.getByText("Waiting for approval")).toBeVisible();
    expect(screen.getByText("Our mapping")).toBeVisible();
    expect(screen.getByRole("button", { name: "Edit your waiting change" })).toBeVisible();
  });

  it("takes a waiting change back", async () => {
    const { handlers, withdrawn } = portal(null, false, {
      needsApproval: true,
      waiting: { theirs: { MappingRules: OURS }, requestedOn: "2026-11-25T09:00:00Z" },
    });
    const { user } = renderApp("/portal/mappings/3", { as: { partner: ACME, permissions: EDITOR }, handlers });

    expect(await screen.findByText("Waiting for approval")).toBeVisible();
    await user.click(screen.getByRole("button", { name: "Take it back" }));
    await waitFor(() => expect(screen.queryByText("Waiting for approval")).not.toBeInTheDocument());
    expect(withdrawn).toEqual([{ subscriptionId: 3 }]);
  });

  it("says why their last change wasn't approved", async () => {
    const { handlers } = portal(null, false, {
      needsApproval: true,
      rejected: { note: "Keep the id field, please.", decidedOn: "2026-11-25T09:00:00Z" },
    });
    renderApp("/portal/mappings/3", { as: { partner: ACME, permissions: EDITOR }, handlers });

    expect(await screen.findByText(/We didn't approve your last change/)).toHaveTextContent("Keep the id field, please.");
  });
});
