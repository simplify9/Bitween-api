import { screen, waitFor } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import {
  addPathRule,
  fill,
  mapperBackend,
  openEditor,
  preview,
  withFormats,
  type SentPreview,
} from "./editorHarness";

/**
 * Making a document readable in the mapping editor: laid out over lines, and coloured.
 *
 * Two halves of one job, split by whether anyone types into the box. A box you type into keeps
 * a real textarea and gets a Format button; a pane you only read gets colour. Nothing gets
 * both, because colouring text under a caret means either a contenteditable or an overlay that
 * has to track the caret exactly.
 *
 * What `formatDocument` and `colourDocument` produce is pinned in
 * src/lib/__tests__/documentPreview.test.ts and documentHighlight.test.ts. These are about the
 * editor offering them in the right places. The mapped document is the server's, so it is mocked
 * here; reading a laid-out document is the engine's job and is pinned in C#, in
 * XmlFormatReadTests (A_prefix_is_not_part_of_the_path,
 * A_subtree_put_back_into_no_namespace_reads_like_any_other) and MappingPreviewTests.
 *
 * ── Laying out a document in a box someone types into ──────────────────────
 *
 * A button rather than the exchange drawer's Raw/Formatted toggle: that shows a document nobody
 * can edit, whereas these boxes hold text belonging to whoever typed it, so reflowing it is an
 * action they take. The button is absent when there is nothing to gain, which is
 * `formatDocument`'s own answer and the thing most worth pinning — it is what stops it offering
 * to mangle a half-typed document.
 */

/** How a partner actually hands over a sample: one line, no spaces. */
const MINIFIED_XML =
  `<s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/"><s:Body>` +
  `<shipping xmlns=""><headerValue><accountNumber>55480501</accountNumber></headerValue></shipping>` +
  `</s:Body></s:Envelope>`;

const MINIFIED_JSON = `{"order":{"customer":"Ali","line":[{"sku":"A1"}]}}`;

const sampleBox = () => screen.getByRole("textbox", { name: "Sample source document" });
const formatButton = () => screen.queryByRole("button", { name: "Format" });
const textOf = (box: HTMLElement) => (box as HTMLTextAreaElement).value;

/** Waits for the preview pane to show `text`, which arrives after the editor's debounce. */
const expectPreview = (text: string) =>
  waitFor(() => expect(preview()).toHaveTextContent(text, { normalizeWhitespace: false }), {
    timeout: 3000,
  });

describe("laying out a sample", () => {
  it("lays out a one-line XML sample, and the tree still reads it", async () => {
    const backend = mapperBackend();
    const { user } = await openEditor(backend);
    await withFormats(user, () => user.selectOptions(screen.getByLabelText("From format"), "xml"));

    await fill(user, sampleBox(), MINIFIED_XML);
    await user.click(formatButton()!);

    // Laid out over lines, with the data untouched.
    expect(textOf(sampleBox())).toMatch(/\n {2}<s:Body>/);
    expect(textOf(sampleBox())).toContain("<accountNumber>55480501</accountNumber>");

    // And it is still the same document as far as the mapping is concerned: the tree offers the
    // same path, and a rule reading it sends the laid-out document to be mapped.
    expect(
      screen.getByRole("button", { name: "Envelope.Body.shipping.headerValue.accountNumber" }),
    ).toBeInTheDocument();
    await addPathRule(user, "account", "Envelope.Body.shipping.headerValue.accountNumber");

    const laidOut = textOf(sampleBox());
    await waitFor(() => expect(backend.lastPreview()?.sourceDocument).toBe(laidOut), { timeout: 3000 });
    expect(backend.lastPreview()!.rules.sourceFormat).toBe("xml");
  });

  it("offers nothing while there is nothing to lay out", async () => {
    const { user } = await openEditor(mapperBackend());

    // Empty, and half-typed: offering to reflow either one could only mangle it.
    expect(formatButton()).not.toBeInTheDocument();
    await fill(user, sampleBox(), `{"order":{"customer":`);
    expect(formatButton()).not.toBeInTheDocument();

    await fill(user, sampleBox(), MINIFIED_JSON);
    expect(formatButton()).toBeVisible();

    // Gone again once the document is already laid out — there is no second press.
    await user.click(formatButton()!);
    expect(textOf(sampleBox())).toMatch(/\n {2}"order": \{/);
    expect(formatButton()).not.toBeInTheDocument();
  });

  it("formatting changes the layout and nothing else", async () => {
    // The invariant that makes the button safe, and the reason it not being undoable is
    // tolerable: it inserts whitespace between tokens and touches nothing else, so the document
    // afterwards maps to exactly what it mapped to before. The server maps what it is sent, so
    // what is checked here is that it is sent the same rules and the same data.
    const backend = mapperBackend({ preview: () => ({ outputDocument: `{\n  "who": "Ali"\n}` }) });
    const { user } = await openEditor(backend);

    await fill(user, sampleBox(), MINIFIED_JSON);
    await addPathRule(user, "who", "order.customer");
    await expectPreview(`"who": "Ali"`);
    const before = preview()!.textContent;
    const asked = backend.lastPreview()!;
    expect(asked.sourceDocument).toBe(MINIFIED_JSON);

    await user.click(formatButton()!);
    expect(textOf(sampleBox())).toMatch(/\n {2}"order": \{/);

    // Asked again, for a document that now reads as several lines instead of one…
    await waitFor(() => expect(backend.lastPreview()!.sourceDocument).not.toBe(MINIFIED_JSON), {
      timeout: 3000,
    });
    const after = backend.lastPreview()!;
    expect(after.sourceDocument).toMatch(/\n {2}"order": \{/);

    // …with the same rules, and the same data.
    expect(after.rules).toEqual(asked.rules);
    expect(JSON.parse(after.sourceDocument)).toEqual(JSON.parse(MINIFIED_JSON));

    // Same output.
    await expectPreview(`"who": "Ali"`);
    expect(preview()!.textContent).toBe(before);
  });

  it("lays out the sample of the output too", async () => {
    const { user } = await openEditor(mapperBackend());

    await user.click(screen.getByRole("button", { name: "Build from a sample of the output" }));
    const target = screen.getByRole("textbox", { name: "Sample output document" });
    await fill(user, target, MINIFIED_JSON);

    await user.click(formatButton()!);
    expect(textOf(target)).toMatch(/\n {2}"order": \{/);
  });
});

describe("colouring the mapped document", () => {
  it("colours it in whichever format it is written", async () => {
    // Answers in the format the rules ask for, as the server does.
    const answer = (sent: SentPreview) =>
      sent.rules.targetFormat === "xml" ? "<order>Ali</order>" : `{\n  "who": "Ali"\n}`;
    const { user } = await openEditor(mapperBackend({ preview: (sent) => ({ outputDocument: answer(sent) }) }));
    await fill(user, sampleBox(), MINIFIED_JSON);
    await addPathRule(user, "who", "order.customer");

    await expectPreview(`"who": "Ali"`);

    // The key and the value are separate things, and the pane says so.
    expect(preview()!.querySelector(".hljs-attr")).toBeInTheDocument();
    expect(preview()!.querySelector(".hljs-string")).toBeInTheDocument();

    // Switching the output to XML colours it as XML, because the mapping declares the format
    // rather than the pane guessing from the text. (Declared beating sniffed is pinned in
    // documentHighlight.test.ts, "prefers a declared format over how the text looks".)
    await withFormats(user, () => user.selectOptions(screen.getByLabelText("To format"), "xml"));
    const name = screen.getAllByRole("textbox", { name: "Output field name" })[0];
    await user.clear(name);
    await user.type(name, "order");

    await waitFor(() => expect(preview()!.querySelector(".hljs-name")).toBeInTheDocument(), {
      timeout: 3000,
    });
  });

  it("shows markup inside a document, and never runs it", async () => {
    // The one place in the app that turns a document into HTML. A partner controls the bytes,
    // so the guarantee is worth checking through the real page and not only in the unit test
    // that pins the escaping.
    const markup = "<script>window.__ran = 1</script>";
    const { user } = await openEditor(
      mapperBackend({ preview: () => ({ outputDocument: `{\n  "note": "${markup}"\n}` }) }),
    );
    await fill(user, sampleBox(), MINIFIED_JSON);

    await user.click(screen.getByRole("button", { name: "Add a field" }));
    await user.type(screen.getAllByRole("textbox", { name: "Output field name" }).at(-1)!, "note");
    await user.click(screen.getAllByRole("radio", { name: "Fixed" }).at(-1)!);
    await user.type(screen.getAllByRole("textbox", { name: "Fixed value" }).at(-1)!, markup);

    // Visible as characters…
    await expectPreview(markup);

    // …and inert: nothing was added to the document, and nothing ran.
    expect(preview()!.querySelector("script")).not.toBeInTheDocument();
    expect((window as unknown as { __ran?: number }).__ran).toBeUndefined();
  });
});
