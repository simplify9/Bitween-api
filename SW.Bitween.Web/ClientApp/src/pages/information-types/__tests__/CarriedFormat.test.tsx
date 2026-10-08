import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { http, HttpResponse } from "msw";
import { MemoryRouter } from "react-router";
import { describe, expect, it } from "vitest";
import { setToken } from "../../../api/http/request";
import { SessionProvider } from "../../../auth/SessionContext";
import { MatchExpressionEditor } from "../../../components/config/MatchExpressionEditor";
import { apiPath, appConfig, profile, renderApp } from "../../../__tests__/support/renderApp";
import { server } from "../../../__tests__/support/server";

/**
 * CSV and Other types are carried, never read: no promoted properties, and no filter that could
 * pass. That the server holds to this is CarriedFormatTests; this is the screens saying so before
 * a save is refused or a filter silently never matches.
 */
const empty = { result: [], totalCount: 0 };

/** GET /documents/{id}: `RawDocument` in src/api/http/documents.ts. */
const typeOf = (documentFormat: string, promotedProperties: { key: string; value: string }[] = []) => ({
  id: 5,
  code: "AGENT_TRACE",
  name: "Agent trace",
  documentFormat,
  busEnabled: false,
  busMessageTypeName: null,
  duplicateInterval: 0,
  disregardsUnfilteredMessages: false,
  promotedProperties,
  usedByCount: 0,
  retiredOn: null,
});

const openType = (type: ReturnType<typeof typeOf>) =>
  renderApp("/information-types/5", {
    handlers: [
      http.get(apiPath("/documents/5"), () => HttpResponse.json(type)),
      ...["/subscriptions", "/busgateways", "/xchanges", "/partners", "/audit"].map((p) =>
        http.get(apiPath(p), () => HttpResponse.json(empty)),
      ),
    ],
  });

describe("an information type switched to a carried format", () => {
  it("has no promoted properties to edit, and says why", async () => {
    openType(typeOf("Csv"));

    expect(await screen.findByText(/None — CSV content isn't read/)).toBeVisible();
    expect(screen.getByText(/Bitween doesn't read the content of this format/)).toBeVisible();
    expect(screen.queryByText("JSON path")).not.toBeInTheDocument();
  });

  it("asks for the promoted properties it still carries to be removed", async () => {
    const { user } = openType(typeOf("Json", [{ key: "orderId", value: "$.order.id" }]));

    await user.selectOptions(await screen.findByLabelText("Payload format"), "Other");

    expect(screen.getByRole("alert")).toHaveTextContent("Still promotes orderId — Other types can't have promoted properties.");
    await user.click(screen.getByRole("button", { name: "Remove them" }));
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });
});

describe("the filter editor on a carried type", () => {
  const renderEditor = (value: Parameters<typeof MatchExpressionEditor>[0]["value"]) => {
    setToken("test-token");
    server.use(appConfig(), profile());
    let cleared = false;
    render(
      <QueryClientProvider client={new QueryClient()}>
        <SessionProvider>
          <MemoryRouter>
            <MatchExpressionEditor
              value={value}
              onChange={(v) => (cleared = v === null)}
              properties={[]}
              disabled={false}
              informationTypeId={5}
              format="Csv"
            />
          </MemoryRouter>
        </SessionProvider>
      </QueryClientProvider>,
    );
    return { user: userEvent.setup(), cleared: () => cleared };
  };

  it("offers no filter at all", async () => {
    renderEditor(null);

    expect(await screen.findByText(/Filters can't be used on CSV information types/)).toBeVisible();
    expect(screen.queryByRole("button", { name: /Add a filter/ })).not.toBeInTheDocument();
    // "Add some" would send you to add promoted properties the type can never have.
    expect(screen.queryByRole("link", { name: "Add some" })).not.toBeInTheDocument();
  });

  it("flags a filter left over from before the switch, and clears it", async () => {
    const { user, cleared } = renderEditor({
      op: "and",
      children: [{ op: "oneOf", path: "$.country", values: ["JO"] }],
    });

    expect(await screen.findByRole("alert")).toHaveTextContent("This filter never passes");
    await user.click(screen.getByRole("button", { name: /Clear filter/ }));
    expect(cleared()).toBe(true);
  });
});
