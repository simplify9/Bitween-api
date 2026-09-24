import { screen, waitFor } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import type { MappingRules } from "../../../lib/nativeMapper/types";
import {
  PARTNER,
  addNamedRule,
  addPathRule,
  expectPreview,
  expectSent,
  fill,
  mapperBackend,
  openWithSample,
  suggestionsFor,
} from "./editorHarness";

/**
 * The toolbar and the keyboard: hiding the preview, choosing whose values it runs with, and
 * undo, redo and save without the mouse.
 */

const customerRule = { target: ["customer"], from: { kind: "path", path: "order.customer" } };
const customerPreview = () => ({ outputDocument: '{\n  "customer": "Ali"\n}' });

describe("the toolbar and the keyboard", { timeout: 20000 }, () => {
  it("gives the rules the whole width when the preview is hidden", async () => {
    const backend = mapperBackend({ preview: customerPreview });
    const { user } = await openWithSample(backend);
    await addPathRule(user, "customer", "order.customer");
    await expectSent(backend, { fields: [customerRule] });
    await expectPreview('"customer": "Ali"');

    await user.click(screen.getByRole("button", { name: "Hide the preview" }));
    expect(screen.queryByText("— what a partner would receive")).not.toBeInTheDocument();

    // Hidden, not switched off: the rules are untouched and it comes back as it was.
    await user.click(screen.getByRole("button", { name: "Show the preview" }));
    await expectPreview('"customer": "Ali"');
    expect(backend.lastPreview().rules).toMatchObject({ fields: [customerRule] });
  });

  it("offers the keys the previewed partner actually has in the partner key box", async () => {
    // Standing in for the server resolving the partner's value, which is
    // MappingPreviewTests.Partner_values_are_available_when_a_partner_is_named (integration) and
    // DocumentMapperTests.PartnerSource_ReadsTheContext.
    const backend = mapperBackend({
      preview: ({ rules, partnerId }) => {
        const key = rules.fields[0]?.from.kind === "partner" ? rules.fields[0].from.key : "";
        const properties: Record<string, string> = partnerId === PARTNER.id ? PARTNER.properties : {};
        return {
          outputDocument: JSON.stringify({ warehouse: properties[key ?? ""] ?? null }, null, 2),
        };
      },
    });
    const { user } = await openWithSample(backend, { order: { customer: "Ali" } });

    await addNamedRule(user, "warehouse");
    const partnerSegments = screen.getAllByRole("radio", { name: "Partner" });
    await user.click(partnerSegments[partnerSegments.length - 1]);

    const key = screen.getByRole("combobox", { name: "Partner property key" });

    // With no partner chosen there is nothing to suggest, and the box is still a box: the mapping
    // runs against whichever partner the exchange belongs to, not this one.
    expect(await suggestionsFor(user, key)).toEqual([]);

    await user.selectOptions(
      screen.getByRole("combobox", { name: "Preview as partner" }),
      screen.getByRole("option", { name: `${PARTNER.name} · 2 properties` }),
    );
    // Fetched for the chosen partner, so the box fills in a moment rather than at once.
    await waitFor(async () =>
      expect(await suggestionsFor(user, key)).toEqual(
        expect.arrayContaining(["WarehouseCode", "SenderId"]),
      ),
    );

    await fill(user, key, "WarehouseCode");
    await expectSent(backend, {
      fields: [{ target: ["warehouse"], from: { kind: "partner", key: "WarehouseCode" } }],
    });
    expect(backend.lastPreview().partnerId).toBe(PARTNER.id);
    await expectPreview('"warehouse": "WH-7"');
    expect(screen.queryByText("⚠")).not.toBeInTheDocument();

    // A key that partner does not have is flagged rather than refused.
    await fill(user, key, "NotAProperty");
    expect(screen.getByText("⚠")).toBeVisible();
  });

  it("undoes, redoes and saves from the keyboard", async () => {
    const backend = mapperBackend({ preview: customerPreview });
    const { user } = await openWithSample(backend);

    await addPathRule(user, "customer", "order.customer");
    const source = screen.getByRole("combobox", { name: "Source field" });
    expect(source).toHaveValue("order.customer");

    // Focus is in a box after typing, and Ctrl+Z there belongs to the box. Clicking the panel's
    // own space takes it back.
    await user.click(screen.getByText("Output", { exact: true }));

    // One step is one change, so this undoes pointing the rule somewhere — not the whole rule,
    // which was three changes ago.
    await user.keyboard("{Control>}z{/Control}");
    expect(source).toHaveValue("");

    await user.keyboard("{Control>}y{/Control}");
    expect(source).toHaveValue("order.customer");

    await user.keyboard("{Control>}z{/Control}");
    await user.keyboard("{Control>}{Shift>}z{/Shift}{/Control}");
    expect(source).toHaveValue("order.customer");

    await user.keyboard("{Control>}s{/Control}");
    expect(await screen.findByText("Saved")).toBeVisible();
    const saved = backend.saves[backend.saves.length - 1];
    expect(saved.mapperId).toBe("NativeMapper");
    expect(JSON.parse(saved.mapperProperties.MappingRules) as MappingRules).toMatchObject({
      fields: [customerRule],
    });
  });

  it("leaves Ctrl+Z inside a box to the box, rather than undoing the mapping", async () => {
    const { user } = await openWithSample(mapperBackend({ preview: customerPreview }));
    await addPathRule(user, "customer", "order.customer");

    await user.click(screen.getByRole("textbox", { name: "Output field name" }));
    await user.keyboard("{Control>}z{/Control}");

    // The rule is still there, still pointed where it was. The old editor took this key in both
    // cases, so fixing a mistyped name meant undoing a change somewhere else entirely.
    expect(screen.getAllByRole("textbox", { name: "Output field name" })).toHaveLength(1);
    expect(screen.getByRole("combobox", { name: "Source field" })).toHaveValue("order.customer");
  });
});
