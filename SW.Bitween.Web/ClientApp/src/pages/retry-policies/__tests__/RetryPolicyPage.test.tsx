import { screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";

/** A retry policy's own page: saving its name and alert writes the groups back as the server has them. */

const group = (whenExhausted: "Fail" | "Hold") => ({
  id: "g-1",
  name: "Timeouts",
  priority: 10,
  enabled: true,
  appliesTo: ["Error"],
  matchers: [{ type: "contains", value: "timeout", caseSensitive: false }],
  action: "Allow",
  budget: { maxAttemptsPerError: 3, maxAttemptsTotal: 10, delayStrategy: { type: "fixed", delayMs: 60000 }, whenExhausted },
  notes: null,
  alertMode: "Inherit",
  alertChannelId: null,
});

describe("a retry policy's page", () => {
  it("doesn't undo a group changed in another tab when its name is saved", async () => {
    const written: { name: string; groups: { budget?: { whenExhausted?: string } }[] }[] = [];
    let reads = 0;
    const { user } = renderApp("/retry-policies/3", {
      handlers: [
        // The page loads the group failing; by the time it saves, another tab has set it to Hold.
        http.get(apiPath("/retrypolicies/3"), () =>
          HttpResponse.json({ name: "Carrier API failures", groups: [group(reads++ === 0 ? "Fail" : "Hold")], alertChannelId: null }),
        ),
        http.get(apiPath("/subscriptions"), () => HttpResponse.json({ result: [], totalCount: 0 })),
        http.get(apiPath("/notificationchannels"), () => HttpResponse.json({})),
        http.post(apiPath("/retrypolicies/3/usage"), () => HttpResponse.json([])),
        http.get(apiPath("/audit"), () => HttpResponse.json({ result: [], totalCount: 0 })),
        http.post(apiPath("/retrypolicies/3"), async ({ request }) => {
          written.push((await request.json()) as (typeof written)[number]);
          return new HttpResponse(null, { status: 204 });
        }),
      ],
    });

    const name = await screen.findByLabelText("Name");
    await user.clear(name);
    await user.type(name, "Carrier failures");
    await user.click(screen.getByRole("button", { name: "Save changes" }));

    await waitFor(() => expect(written).toHaveLength(1));
    expect(written[0].name).toBe("Carrier failures");
    expect(written[0].groups[0].budget?.whenExhausted).toBe("Hold");
  });
});
