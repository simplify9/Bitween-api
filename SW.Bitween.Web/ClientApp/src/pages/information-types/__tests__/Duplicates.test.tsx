import { screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";

/**
 * An information type's duplicate window: the drop switch appears with a window, is sent as set,
 * and the week's count says what dropping would have done. What the server does is
 * SW.Bitween.IntegrationTests' business (DuplicateArrivalTests).
 */
const empty = { result: [], totalCount: 0 };

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

describe("an information type's duplicates", () => {
  it("offers dropping them only once there is a window", async () => {
    openType(typeOf(), []);
    expect(await screen.findByLabelText(/^Duplicate window/)).toBeVisible();
    expect(screen.queryByRole("checkbox", { name: /Drop duplicates/ })).toBeNull();
  });

  it("says what dropping would have done, and sends the switch", async () => {
    const saved: Record<string, unknown>[] = [];
    const { user } = openType(typeOf({ duplicateInterval: 30, dropDuplicates: false, duplicatesLastWeek: 4, duplicatesDroppedLastWeek: 0 }), saved);

    expect(await screen.findByText(/4 duplicates in the last 7 days, none dropped/)).toBeVisible();
    await user.click(screen.getByRole("checkbox", { name: /Drop duplicates/ }));
    await user.click(screen.getByRole("button", { name: /save/i }));

    await expect.poll(() => saved).toHaveLength(1);
    expect(saved[0]).toMatchObject({ duplicateInterval: 30, dropDuplicates: true });
  });
});
