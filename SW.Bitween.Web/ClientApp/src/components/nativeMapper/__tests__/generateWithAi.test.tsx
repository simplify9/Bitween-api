import { screen, waitFor, within } from "@testing-library/react";
import { delay, http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";
import type { MappingRules } from "../../../lib/nativeMapper/types";
import { SAMPLE, SUBSCRIPTION_ID, addPathRule, expectSent, fill, mapperBackend, type MapperBackend } from "./editorHarness";

/**
 * Generate with AI: a dialog takes both samples and any instructions, the server writes the
 * rules and checks them, and the editor puts them in place of its own as one step Undo takes
 * back. What the model is asked and how the check works is the C# suite's business
 * (MappingSuggestionTests); this is what the editor does around it.
 */

const customerRule = { target: ["customer"], from: { kind: "path", path: "order.customer" } };
const TARGET = '{ "customer": "Ali" }';

interface SentSuggestion {
  rules: MappingRules;
  sourceDocument: string;
  targetDocument: string;
  refine: boolean;
  instructions?: string;
}

interface Answer {
  rules: object;
  differences?: { target: string; reason: string }[];
}

function suggestions(answer: (sent: SentSuggestion) => Answer | Promise<Answer>) {
  const sent: SentSuggestion[] = [];
  const handler = http.post(apiPath("/mappingsuggestions"), async ({ request }) => {
    const body = (await request.json()) as Omit<SentSuggestion, "rules"> & { mappingRules: string };
    const suggestion = { ...body, rules: JSON.parse(body.mappingRules) as MappingRules, refine: Boolean(body.refine) };
    sent.push(suggestion);
    const reply = await answer(suggestion);
    return HttpResponse.json({
      mappingRules: JSON.stringify({ version: 1, sourceFormat: "json", targetFormat: "json", ...reply.rules }),
      differences: reply.differences ?? [],
      matchesTarget: (reply.differences ?? []).length === 0,
      model: "claude-sonnet-5-5",
    });
  });
  return { handler, sent };
}

async function open(backend: MapperBackend, aiConfigured: boolean, extra: ReturnType<typeof suggestions>["handler"][] = []) {
  const app = renderApp(`/subscriptions/${SUBSCRIPTION_ID}/mapper`, {
    handlers: [...backend.handlers, ...extra],
    config: { aiConfigured },
  });
  await screen.findByRole("button", { name: "Save" }, { timeout: 5000 });
  return app;
}

/** Opens the dialog with the source sample already pasted in the editor, and fills in the output one. */
async function openDialog(user: ReturnType<typeof renderApp>["user"]) {
  await fill(user, screen.getByRole("textbox", { name: "Sample source document" }), JSON.stringify(SAMPLE, null, 2));
  await user.click(screen.getByRole("button", { name: /Generate with AI/ }));
  const dialog = await screen.findByRole("dialog", { name: "Generate the rules with AI" });
  // The editor's own sample is the dialog's: one sample, wherever it is typed.
  expect(JSON.parse((within(dialog).getByRole("textbox", { name: "Source sample" }) as HTMLTextAreaElement).value)).toEqual(SAMPLE);
  await fill(user, within(dialog).getByRole("textbox", { name: "Output sample" }), TARGET);
  return dialog;
}

describe("Generate with AI", { timeout: 20000 }, () => {
  it("says where to turn it on until Settings has a provider key", async () => {
    const { user } = await open(mapperBackend(), false);

    await user.click(screen.getByRole("button", { name: /Generate with AI/ }));

    const dialog = await screen.findByRole("dialog", { name: "Generate the rules with AI" });
    expect(within(dialog).getByText(/isn't set up/)).toBeInTheDocument();
    const link = within(dialog).getByRole("link", { name: "Open Settings, AI in a new tab" });
    expect(link).toHaveAttribute("href", "/settings?section=AI");
    expect(link).toHaveAttribute("target", "_blank");
  });

  it("replaces the rules with the generated ones, says so, and Undo puts them back", async () => {
    const backend = mapperBackend({ preview: () => ({ outputDocument: '{\n  "customer": "Ali"\n}' }) });
    const ai = suggestions(() => ({ rules: { fields: [customerRule] } }));
    const { user } = await open(backend, true, [ai.handler]);
    await addPathRule(user, "reference", "order.ref");

    const dialog = await openDialog(user);
    expect(within(dialog).getByText(/Replaces the 1 rule there is/)).toBeInTheDocument();
    expect(within(dialog).getByText(/never the values/)).toBeInTheDocument();
    await user.type(within(dialog).getByRole("textbox", { name: /Instructions/ }), "Names stay as they are.");
    await user.click(within(dialog).getByRole("button", { name: /^Generate$/ }));

    await screen.findByText("Matches the output sample");
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(screen.getByText(/Replaced 1 rule with 1/)).toBeInTheDocument();
    expect(screen.getByText(/claude-sonnet-5-5 · \d+s/)).toBeInTheDocument();
    await expectSent(backend, { fields: [customerRule] });
    expect(ai.sent[0]).toMatchObject({ targetDocument: TARGET, refine: false, instructions: "Names stay as they are." });

    await user.click(screen.getByRole("button", { name: "Undo the generated rules" }));
    await expectSent(backend, { fields: [{ target: ["reference"], from: { kind: "path", path: "order.ref" } }] });
    expect(screen.queryByText("Matches the output sample")).not.toBeInTheDocument();
  });

  it("marks the rows that miss the output sample, shows one on click, and fixes them on request", async () => {
    const backend = mapperBackend();
    const fixed = { target: ["customer"], from: { kind: "fixed", value: "Ali" } };
    const ai = suggestions(({ refine }) =>
      refine
        ? { rules: { fields: [customerRule] } }
        : { rules: { fields: [fixed] }, differences: [{ target: "customer", reason: "the rules write null" }] },
    );
    const { user } = await open(backend, true, [ai.handler]);

    const dialog = await openDialog(user);
    await user.click(within(dialog).getByRole("button", { name: /^Generate$/ }));

    // The row says it too, not only the list in the toolbar.
    expect(await screen.findByText("customer: the rules write null")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Where the generated rules miss the output sample" }));
    await user.click(screen.getByRole("button", { name: /customer: the rules write null/ }));
    const row = screen.getByRole("textbox", { name: "Output field name" }).closest("[data-error-key]")!;
    expect(row).toHaveAttribute("data-error-key", "customer");
    expect(row.className).toContain("border-crimson-300");

    await user.click(screen.getByRole("button", { name: /Fix with AI/ }));

    await screen.findByText("Matches the output sample");
    // The fix starts from the rules the editor holds, not from nothing.
    expect(ai.sent[1]).toMatchObject({ refine: true, rules: { fields: [fixed] } });
    await expectSent(backend, { fields: [customerRule] });
  });

  it("drops an answer when a sample changed while it was being written", async () => {
    const backend = mapperBackend();
    let answer!: () => void;
    const released = new Promise<void>((resolve) => (answer = resolve));
    const ai = suggestions(async () => {
      await released;
      return { rules: { fields: [customerRule] } };
    });
    const { user } = await open(backend, true, [ai.handler]);

    const dialog = await openDialog(user);
    await user.click(within(dialog).getByRole("button", { name: /^Generate$/ }));
    await within(dialog).findByRole("status");
    await user.type(within(dialog).getByRole("textbox", { name: "Output sample" }), " ");
    answer();

    expect(await within(dialog).findByText(/changed while the AI was working/)).toBeInTheDocument();
    expect(screen.queryByText("Matches the output sample")).not.toBeInTheDocument();
    expect(screen.getByText(/0 rules/)).toBeInTheDocument();
  });

  it("can be cancelled while it waits, leaving the rules alone", async () => {
    const backend = mapperBackend();
    const ai = suggestions(async () => {
      await delay("infinite");
      return { rules: { fields: [customerRule] } };
    });
    const { user } = await open(backend, true, [ai.handler]);

    const dialog = await openDialog(user);
    await user.click(within(dialog).getByRole("button", { name: /^Generate$/ }));
    expect(await within(dialog).findByRole("status")).toHaveTextContent(/Generating… \d+s/);

    await user.click(within(dialog).getByRole("button", { name: "Cancel" }));

    expect(within(dialog).getByRole("button", { name: /^Generate$/ })).toBeEnabled();
    await waitFor(() => expect(ai.sent).toHaveLength(1));
    expect(screen.queryByText("Matches the output sample")).not.toBeInTheDocument();
    expect(screen.getByText(/0 rules/)).toBeInTheDocument();
  });
});
