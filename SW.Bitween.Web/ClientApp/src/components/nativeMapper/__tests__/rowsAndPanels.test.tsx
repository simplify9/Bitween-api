import { screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import {
  addList,
  addListField,
  addPathRule,
  expectPreview,
  expectSent,
  mapperBackend,
  openDetail,
  openWithSample,
  preview,
} from "./editorHarness";

/**
 * Reading and reaching into a big mapping: what a click on a row opens, what the source tree
 * shows, and the checkboxes that sit inside a clickable row.
 */

const transform = () => screen.queryByRole("combobox", { name: "Transform" });

describe("the rows and the panels", { timeout: 20000 }, () => {
  it("shows what is behind a row's chevron when the row is clicked", async () => {
    const { user } = await openWithSample(mapperBackend());

    await addPathRule(user, "total", "order.net");

    // The transform and the type live behind the chevron, and finding the chevron was the whole
    // complaint: the row itself is the obvious thing to click.
    expect(transform()).not.toBeInTheDocument();

    await user.click(screen.getByRole("textbox", { name: "Output field name" }));
    expect(transform()).not.toBeInTheDocument();

    // Clicking the row's own space, rather than a control in it.
    await user.click(screen.getAllByText("←")[0]);
    expect(transform()).toBeVisible();

    await user.click(screen.getAllByText("←")[0]);
    expect(transform()).not.toBeInTheDocument();
  });

  it("shows the fields inside a list in the source tree, named as a rule names them", async () => {
    const { user } = await openWithSample(mapperBackend());

    // `sku` sits inside `order.line`, and a rule in a list over that list reads it as `sku`.
    // Hiding these meant the only way to see what was in a list was to read the sample somewhere
    // else.
    expect(screen.getByRole("button", { name: "sku" })).toBeVisible();
    expect(screen.getByRole("button", { name: "qty" })).toBeVisible();

    await user.click(screen.getByRole("button", { name: "Collapse order.line" }));
    expect(screen.queryByRole("button", { name: "sku" })).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Expand order.line" }));
    expect(screen.getByRole("button", { name: "sku" })).toBeVisible();
  });

  it("ticks a checkbox in a list's settings by its text", async () => {
    // Standing in for the engine's filter, which is DocumentMapperTests.Loop_Filter_SkipsItems
    // and Loop_EveryFilterOperator.
    const backend = mapperBackend({
      preview: ({ rules }) => ({
        outputDocument: JSON.stringify(
          {
            lines:
              rules.lists[0]?.where?.operator === "greaterThan"
                ? [{ qty: 2 }]
                : [{ qty: 2 }, { qty: 0 }],
          },
          null,
          2,
        ),
      }),
    });
    const { user } = await openWithSample(backend);

    const lines = await addList(user, "lines", "order.line");
    await addListField(user, lines, "lines", "qty", "qty");
    await user.click(screen.getByRole("button", { name: "Settings for the list lines" }));

    // Clicking the words, not the box — which is what anyone does, and what a test that checks
    // the input directly never exercises. The row click that opens these settings used to swallow
    // it: the box ticked and the panel folded away in the same tick, so it looked like the click
    // did nothing.
    await user.click(screen.getByText("Only some entries"));

    expect(screen.getByRole("textbox", { name: "Filter field" })).toBeVisible();
    await user.type(screen.getByRole("textbox", { name: "Filter field" }), "qty");
    await user.selectOptions(screen.getByRole("combobox", { name: "Filter comparison" }), "greaterThan");
    await user.type(screen.getByRole("textbox", { name: "Filter value" }), "0");

    await expectSent(backend, {
      lists: [
        {
          target: ["lines"],
          over: "order.line",
          where: { field: "qty", operator: "greaterThan", value: "0" },
          fields: [{ target: ["qty"], from: { kind: "path", path: "qty" } }],
        },
      ],
    });
    // The entry with qty 0 is gone, which is the whole point of the checkbox.
    await expectPreview('"qty": 2');
    expect(preview()).not.toHaveTextContent('"qty": 0');
  });

  it("ticks a checkbox in a rule's detail by its text", async () => {
    // Standing in for the engine's table, which is DocumentMapperTests.Lookup_SubstitutesAValue.
    const backend = mapperBackend({
      preview: ({ rules }) => ({
        outputDocument: JSON.stringify(
          { countryName: rules.fields[0]?.lookup?.table?.JO ?? "JO" },
          null,
          2,
        ),
      }),
    });
    const { user } = await openWithSample(backend, { country: "JO" });

    await addPathRule(user, "countryName", "country");
    await openDetail(user, "countryName");

    await user.click(screen.getByText("Substitute values from a table"));
    expect(screen.getByRole("button", { name: "Add incoming value" })).toBeVisible();

    await user.click(screen.getByRole("button", { name: "Add incoming value" }));
    await user.type(screen.getByRole("textbox", { name: "Incoming value 1" }), "JO");
    await user.type(screen.getByRole("textbox", { name: "Becomes 1" }), "Jordan");

    await expectSent(backend, {
      fields: [
        {
          target: ["countryName"],
          from: { kind: "path", path: "country" },
          lookup: { table: { JO: "Jordan" } },
        },
      ],
    });
    await expectPreview('"countryName": "Jordan"');
  });
});
