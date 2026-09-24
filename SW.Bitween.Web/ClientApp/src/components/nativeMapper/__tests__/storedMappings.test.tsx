import { screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import type { MappingRules } from "../../../lib/nativeMapper/types";
import {
  addFixedRule,
  addPathRule,
  expectPreview,
  expectSent,
  mapperBackend,
  type MapperBackend,
  mountEditor,
  openDetail,
  openEditor,
  openWithSample,
  reopen,
} from "./editorHarness";

/**
 * What the editor does with a mapping that is already stored: refuse the ones it cannot read, show
 * the ones it can exactly as they were saved, and save back what it was given.
 *
 * Reading the stored rules is src/lib/nativeMapper/__tests__/rules.test.ts ("saving and loading",
 * "rules saved before lists were renamed"). These are about what that looks like on the page, and
 * what a save sends.
 */

const savedRules = (backend: MapperBackend) =>
  JSON.parse(backend.saves[backend.saves.length - 1].mapperProperties.MappingRules) as MappingRules;

const REFUSED = [
  {
    what: "are not readable at all",
    rules: "{ this is not json",
    says: /could not be read/,
  },
  {
    what: "come from a newer version of Bitween",
    rules: JSON.stringify({ version: 99, fields: [], lists: [] }),
    says: /version 99/,
  },
  {
    what: "were saved before lists were renamed",
    rules: JSON.stringify({ version: 1, fields: [], loops: [{ over: "x", target: ["y"] }] }),
    says: /before lists were renamed/,
  },
];

describe("a stored mapping", { timeout: 20000 }, () => {
  it.each(REFUSED)("refuses to open rules that $what, rather than starting blank", async ({ rules, says }) => {
    const backend = mapperBackend({ mapperProperties: { MappingRules: rules } });
    // Mounted without waiting for Save, which is the thing a refused mapping never shows.
    mountEditor(backend);

    // Opening blank and letting someone press Save would replace a working mapping with nothing,
    // which is worse than refusing to open.
    expect(await screen.findByText(says, {}, { timeout: 5000 })).toBeVisible();
    expect(screen.queryByRole("button", { name: "Save" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Back to the subscription" })).toBeVisible();
  });

  it("still shows a date format the dropdown never offered, and saves it back", async () => {
    // The engine formats with any .NET pattern, so a saved mapping can hold one this closed list
    // does not offer — set through the API, or offered here under a label that has since changed.
    // A select with no matching option shows nothing selected, which reads as "no format chosen".
    const backend = mapperBackend({
      mapperProperties: {
        MappingRules: JSON.stringify({
          version: 1,
          sourceFormat: "json",
          targetFormat: "json",
          fields: [
            {
              target: ["shipped"],
              from: { kind: "path", path: "order.date" },
              transform: { fn: "formatDate", format: "d MMMM" },
            },
          ],
          lists: [],
        }),
      },
    });
    const { user } = await openEditor(backend);
    await openDetail(user, "shipped");

    expect(screen.getByRole("combobox", { name: "Format a date — Format" })).toHaveValue("d MMMM");

    // And saving the mapping for some unrelated reason must not quietly replace it.
    await addFixedRule(user, "channel", "web");
    await user.click(screen.getByRole("button", { name: "Save" }));
    expect(await screen.findByText("Saved")).toBeVisible();
    expect(savedRules(backend).fields).toMatchObject([
      { target: ["shipped"], transform: { fn: "formatDate", format: "d MMMM" } },
      { target: ["channel"], from: { kind: "fixed", value: "web" } },
    ]);

    const reopened = await reopen(backend);
    await openDetail(reopened.user, "shipped");
    expect(screen.getByRole("combobox", { name: "Format a date — Format" })).toHaveValue("d MMMM");
  });

  it("makes a date that could be read two ways say which", async () => {
    // What the server answers, standing in for the engine. The engine's side of this —
    // refused at year-first, read either way round otherwise — is
    // DocumentMapperTests.SourceDateOrder_DecidesHowTheTransformsReadADate and the
    // TransformsTests.FormatDate_* cases, whose refusal message this is.
    const backend = mapperBackend({
      preview: ({ rules }) => {
        const format = rules.fields[0]?.transform?.format;
        if (!format) return { outputDocument: '{\n  "shipDate": "04.09.2026"\n}' };
        if (!rules.sourceDateOrder || rules.sourceDateOrder === "yearFirst")
          return {
            ruleErrors: [
              {
                target: "shipDate",
                reason:
                  "formatDate could not read '04.09.2026' as a date. If the day or the month comes " +
                  "first, say so under the source document.",
              },
            ],
          };
        return {
          outputDocument: `{\n  "shipDate": "${rules.sourceDateOrder === "dayFirst" ? "2026-09-04" : "2026-04-09"}"\n}`,
        };
      },
    });

    // A real CargoNet shipping date: the 4th of September, French style.
    const { user } = await openWithSample(backend, { order: { shippingdate: "04.09.2026" } });

    await addPathRule(user, "shipDate", "order.shippingdate");
    await openDetail(user, "shipDate");
    await user.selectOptions(screen.getByRole("combobox", { name: "Transform" }), "formatDate");

    // One control on the row, and it is a closed list: what the date should look like on the way
    // out, shown as the date itself rather than as yyyy-MM-dd letters.
    const format = screen.getByRole("combobox", { name: "Format a date — Format" });
    const offered = within(format).getAllByRole("option").map((o) => o.textContent);
    expect(offered.slice(0, 3)).toEqual(["Format…", "2026-09-04", "04/09/2026"]);
    await user.selectOptions(format, "yyyy-MM-dd");

    // Refused rather than guessed. The invariant parser reads this as the 9th of April perfectly
    // happily, which would date a shipment five months out with nothing said. Year-first is the
    // default, so it is left off what is sent.
    await expectSent(backend, {
      fields: [{ target: ["shipDate"], transform: { fn: "formatDate", format: "yyyy-MM-dd" } }],
    });
    expect(backend.lastPreview().rules.sourceDateOrder).toBeUndefined();
    expect((await screen.findAllByText(/could not read '04\.09\.2026'/))[0]).toBeVisible();

    // Answered once for the document, under the sample it describes — a partner writes dates one
    // way throughout, so this is not a per-rule question.
    const dates = screen.getByRole("combobox", { name: "Dates in the incoming document" });
    expect(within(dates).getAllByRole("option").map((o) => o.textContent)).toEqual([
      "Year first — 2026-09-04",
      "Day first — 04.09.2026",
      "Month first — 09.04.2026",
    ]);

    await user.selectOptions(dates, "dayFirst");
    await expectSent(backend, { sourceDateOrder: "dayFirst" });
    await expectPreview('"shipDate": "2026-09-04"');

    // And the other way round, from the same characters.
    await user.selectOptions(dates, "monthFirst");
    await expectSent(backend, { sourceDateOrder: "monthFirst" });
    await expectPreview('"shipDate": "2026-04-09"');

    // It is part of the mapping, so it is saved with it and comes back with it.
    await user.click(screen.getByRole("button", { name: "Save" }));
    expect(await screen.findByText("Saved")).toBeVisible();
    expect(savedRules(backend).sourceDateOrder).toBe("monthFirst");

    await reopen(backend);
    expect(screen.getByRole("combobox", { name: "Dates in the incoming document" })).toHaveValue(
      "monthFirst",
    );
  });
});
