import { screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import {
  addList,
  addListField,
  addPathRule,
  expectPreview,
  expectSent,
  listGroup,
  mapperBackend,
  openWithSample,
  preview,
} from "./editorHarness";

/**
 * Building a mapping up and taking it apart again in the rendered editor.
 *
 * The reducer underneath has its own tests (src/lib/nativeMapper/__tests__), and the engine the
 * preview runs is the C# suite's. What neither can see is whether the rows on screen do what they
 * say — a remove button that removes, a fold that only folds — and whether what the editor then
 * asks the server to map is still the mapping on screen.
 */

const nameBoxes = () => screen.queryAllByRole("textbox", { name: "Output field name" });

describe("editing the rules", { timeout: 20000 }, () => {
  it("removes a field, a list, and a written entry", async () => {
    const backend = mapperBackend({
      preview: ({ rules }) => ({
        outputDocument:
          rules.fields.length + rules.lists.length === 0
            ? "{}"
            : JSON.stringify({ customer: "Ali", lines: [{ code: "A1" }, { code: "B7" }] }, null, 2),
      }),
    });
    const { user } = await openWithSample(backend);

    await addPathRule(user, "customer", "order.customer");
    const lines = await addList(user, "lines", "order.line");
    await addListField(user, lines, "lines", "code", "sku");
    await user.click(screen.getByRole("button", { name: "Add an entry to lines" }));

    await expectSent(backend, {
      fields: [{ target: ["customer"], from: { kind: "path", path: "order.customer" } }],
      lists: [
        {
          target: ["lines"],
          over: "order.line",
          fields: [{ target: ["code"], from: { kind: "path", path: "sku" } }],
          fixed: [{ fields: [], lists: [] }],
        },
      ],
    });
    await expectPreview('"code": "A1"');

    await user.click(screen.getByRole("button", { name: "Remove entry 1" }));
    expect(screen.queryByRole("group", { name: "entry 1" })).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Remove the rule for code" }));
    await user.click(screen.getByRole("button", { name: "Remove the list lines" }));
    await user.click(screen.getByRole("button", { name: "Remove the rule for customer" }));

    expect(screen.getByText(/No rules yet\./)).toBeVisible();
    // Removing every rule is a mapping that produces an empty document, not a failure — so it is
    // still sent to be mapped, and what comes back is shown. That the engine answers `{}` is
    // DocumentMapperTests.EmptyRules_ProduceAnEmptyDocument.
    await expectSent(backend, { fields: [], lists: [] });
    await expectPreview("{}");
    expect(preview()).toHaveTextContent(/^\{\}$/);
  });

  it("puts back a removed rule on undo", async () => {
    const backend = mapperBackend({
      preview: ({ rules }) => ({
        outputDocument: rules.fields.length ? '{\n  "customer": "Ali"\n}' : "{}",
      }),
    });
    const { user } = await openWithSample(backend);

    await addPathRule(user, "customer", "order.customer");
    await expectSent(backend, {
      fields: [{ target: ["customer"], from: { kind: "path", path: "order.customer" } }],
    });
    await expectPreview('"customer": "Ali"');

    await user.click(screen.getByRole("button", { name: "Remove the rule for customer" }));
    expect(nameBoxes()).toHaveLength(0);

    await user.click(screen.getByRole("button", { name: "Undo" }));
    expect(screen.getByRole("textbox", { name: "Output field name" })).toHaveValue("customer");

    await user.click(screen.getByRole("button", { name: "Redo" }));
    expect(nameBoxes()).toHaveLength(0);
  });

  it("keeps the branches above a match when the output is searched", async () => {
    const backend = mapperBackend({
      preview: () => ({
        outputDocument: JSON.stringify(
          { billing: { city: "Amman", country: "JO" }, name: "Ali" },
          null,
          2,
        ),
      }),
    });
    const { user } = await openWithSample(backend, { c: "Amman", k: "JO", n: "Ali" });

    await addPathRule(user, "billing.city", "c");
    await addPathRule(user, "billing.country", "k");
    await addPathRule(user, "name", "n");
    const mapping = {
      fields: [
        { target: ["billing", "city"], from: { kind: "path", path: "c" } },
        { target: ["billing", "country"], from: { kind: "path", path: "k" } },
        { target: ["name"], from: { kind: "path", path: "n" } },
      ],
    };
    await expectSent(backend, mapping);
    const sent = backend.previews.length;

    await user.type(screen.getByRole("textbox", { name: "Search output fields" }), "city");

    // The match is reachable, which means the object above it survives too.
    expect(screen.getByRole("group", { name: "Fields inside billing" })).toBeVisible();
    expect(nameBoxes()).toHaveLength(1);
    expect(nameBoxes()[0]).toHaveValue("city");

    // Searching does not change the mapping — only what is shown of it. Nothing new is sent to
    // be mapped, and the whole document, `name` included, is still what the preview shows.
    await expectPreview('"name": "Ali"');
    expect(backend.previews).toHaveLength(sent);
    expect(backend.lastPreview().rules).toMatchObject(mapping);

    await user.clear(screen.getByRole("textbox", { name: "Search output fields" }));
    expect(nameBoxes()).toHaveLength(3);
  });

  it("folds a list away without losing what is inside it", async () => {
    const backend = mapperBackend({
      preview: () => ({
        outputDocument: JSON.stringify({ lines: [{ code: "A1" }, { code: "B7" }] }, null, 2),
      }),
    });
    const { user } = await openWithSample(backend);

    const lines = await addList(user, "lines", "order.line");
    await addListField(user, lines, "lines", "code", "sku");
    const mapping = {
      lists: [
        {
          target: ["lines"],
          over: "order.line",
          fields: [{ target: ["code"], from: { kind: "path", path: "sku" } }],
        },
      ],
    };
    await expectSent(backend, mapping);
    await expectPreview('"code": "A1"');
    const sent = backend.previews.length;

    await user.click(screen.getByRole("button", { name: "Collapse the list lines" }));
    expect(screen.queryByRole("group", { name: "Rules for the list lines" })).not.toBeInTheDocument();
    // Folded, not removed: the mapping is the one already sent, and it still produces the same
    // document.
    expect(preview()).toHaveTextContent('"code": "A1"');
    expect(backend.previews).toHaveLength(sent);

    await user.click(screen.getByRole("button", { name: "Expand the list lines" }));
    expect(
      within(listGroup("lines")).getByRole("textbox", { name: "Output field name" }),
    ).toHaveValue("code");
  });
});
