import { screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";

/**
 * The schema an information type can carry: shown, edited and sent as it is, and removable.
 * What the server does with it is SW.Bitween.IntegrationTests' business (DocumentSchemaValidationTests).
 */
const empty = { result: [], totalCount: 0 };
const SCHEMA = '{ "type": "object", "required": ["orderId"] }';

/** GET /documents/{id}: `RawDocument` in src/api/http/documents.ts. */
const typeOf = (fields: Record<string, unknown> = {}) => ({
  id: 5,
  code: "ORDER",
  name: "Order",
  documentFormat: "Json",
  busEnabled: false,
  busMessageTypeName: null,
  duplicateInterval: 0,
  disregardsUnfilteredMessages: false,
  promotedProperties: [],
  validationSchema: null,
  usedByCount: 0,
  retiredOn: null,
  ...fields,
});

const openType = (type: ReturnType<typeof typeOf>, saved: Record<string, unknown>[]) =>
  renderApp("/information-types/5", {
    handlers: [
      http.get(apiPath("/documents/5"), () => HttpResponse.json(type)),
      http.post(apiPath("/documents/5"), async ({ request }) => {
        saved.push((await request.json()) as Record<string, unknown>);
        return new HttpResponse(null, { status: 204 });
      }),
      ...["/subscriptions", "/busgateways", "/xchanges", "/partners", "/audit"].map((p) =>
        http.get(apiPath(p), () => HttpResponse.json(empty)),
      ),
    ],
  });

describe("an information type's schema", () => {
  it("is saved as typed", async () => {
    const saved: Record<string, unknown>[] = [];
    const { user } = openType(typeOf(), saved);

    const box = await screen.findByRole("textbox", { name: "Schema" });
    await user.click(box);
    await user.paste(SCHEMA);
    await user.click(screen.getByRole("button", { name: /save/i }));

    await expect.poll(() => saved).toHaveLength(1);
    expect(saved[0].validationSchema).toBe(SCHEMA);
  });

  it("is kept by a save that changes something else", async () => {
    const saved: Record<string, unknown>[] = [];
    const { user } = openType(typeOf({ validationSchema: SCHEMA }), saved);

    expect(await screen.findByRole("textbox", { name: "Schema" })).toHaveValue(SCHEMA);
    await user.type(screen.getByLabelText(/^Name/), "s");
    await user.click(screen.getByRole("button", { name: /save/i }));

    await expect.poll(() => saved).toHaveLength(1);
    expect(saved[0].validationSchema).toBe(SCHEMA);
  });

  it("is removed when emptied", async () => {
    const saved: Record<string, unknown>[] = [];
    const { user } = openType(typeOf({ validationSchema: SCHEMA }), saved);

    await user.clear(await screen.findByRole("textbox", { name: "Schema" }));
    await user.click(screen.getByRole("button", { name: /save/i }));

    await expect.poll(() => saved).toHaveLength(1);
    // Empty, not missing: the server reads a missing schema as "leave it alone".
    expect(saved[0].validationSchema).toBe("");
  });

  it("can't stay on a type whose content isn't read", async () => {
    const saved: Record<string, unknown>[] = [];
    openType(typeOf({ documentFormat: "Csv", validationSchema: SCHEMA }), saved);

    expect(await screen.findByText(/Still has a schema/)).toBeVisible();
    expect(screen.queryByRole("textbox", { name: "Schema" })).not.toBeInTheDocument();
  });
});
