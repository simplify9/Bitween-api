import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse, type JsonBodyType } from "msw";
import { describe, expect, it } from "vitest";
import { apiPath } from "../../../__tests__/support/renderApp";
import { server } from "../../../__tests__/support/server";
import { AdapterConfig } from "../AdapterConfig";
import { sourceDocument, type SourceDocument } from "../sourceValues";

const json = (path: string, body: JsonBodyType) => http.get(apiPath(path), () => HttpResponse.json(body));

/** GET /adapters/Catalog: one handler with one required setting. */
const handlerCatalog = json("/adapters/Catalog", [
  {
    key: "NativeHttpHandler",
    native: true,
    versions: [],
    startupValues: { Url: { optional: false, default: null, private: false, description: null } },
  },
]);

const show = (url: string, sourceValues?: SourceDocument | null) => {
  server.use(
    handlerCatalog,
    json("/globaladaptervaluessets", { result: [], totalCount: 0 }),
    json("/partners", { result: [], totalCount: 0 }),
  );
  return render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <AdapterConfig
        kind="handler"
        adapterId="NativeHttpHandler"
        properties={{ Url: url }}
        onChange={() => {}}
        disabled={false}
        sourceValues={sourceValues}
      />
    </QueryClientProvider>,
  );
};

/** One feeder whose last document had an order number and nothing else. */
const fedBySendOrders = sourceDocument(true, [
  { id: 1, name: "Send orders", xchangeId: "x1", paths: [{ path: "order.number", example: "SO-1001" }] },
]);

/**
 * A response subscription's handler can read the original document as `{{source.PATH}}`. The
 * paths are offered from the last document each feeder received, with its values shown as
 * examples, and one it lacked is a warning, not a refusal: documents vary.
 */
describe("AdapterConfig source values", () => {
  it("offers the paths the last document had, shown as examples, and warns about one it didn't", async () => {
    show("http://host/{{source.order.number}}/{{source.order.warehouse}}", fedBySendOrders);

    // The value the last document had is shown as an example, not as the value.
    expect(await screen.findByText("SO-1001")).toBeInTheDocument();
    expect(screen.getByText(/each exchange reads its own/)).toBeInTheDocument();
    expect(screen.getByText(/Not in the last document "Send orders" received/)).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Insert a reference into Url" }));
    expect(screen.getByText("Original document")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /source\.order\.number/ })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /source\.order\.warehouse/ })).not.toBeInTheDocument();
  });

  it("checks nothing while nothing feeds it yet", async () => {
    show("http://host/{{source.order.warehouse}}", sourceDocument(false, []));

    expect(await screen.findByText(/nothing feeds this yet/)).toBeInTheDocument();
    expect(screen.queryByText(/Not in the last document/)).not.toBeInTheDocument();
  });

  it("checks nothing while what feeds it hasn't run yet", async () => {
    show("http://host/{{source.order.warehouse}}", sourceDocument(true, []));

    expect(await screen.findByText(/nothing has run yet/)).toBeInTheDocument();
    expect(screen.queryByText(/Not in the last document/)).not.toBeInTheDocument();
  });

  it("says nothing about the token on any other subscription, which is never handed source values", async () => {
    show("http://host/{{source.order.number}}");

    expect(await screen.findByDisplayValue("http://host/{{source.order.number}}")).toBeInTheDocument();
    expect(screen.queryByText(/original document/i)).not.toBeInTheDocument();
  });
});
