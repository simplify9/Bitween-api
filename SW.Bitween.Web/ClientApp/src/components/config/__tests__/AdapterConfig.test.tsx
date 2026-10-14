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

/** An HTTP-like handler: one required setting and a spread of optional ones, one with long help. */
const busyCatalog = json("/adapters/Catalog", [
  {
    key: "NativeHttpHandler",
    native: true,
    versions: [],
    startupValues: {
      Url: { optional: false, default: null, private: false, description: null },
      Verb: { optional: true, default: "post", private: false, description: null },
      ContentType: { optional: true, default: "application/json", private: false, description: null },
      Headers: { optional: true, default: null, private: true, description: null },
      ApiKey: { optional: true, default: null, private: true, description: null },
      LoginBody: {
        optional: true,
        default: null,
        private: false,
        description:
          "The body posted to the login URL to get a token. It can reference partner properties and global values, and is sent as JSON unless the content type says otherwise. Leave it empty when the API takes a key instead.",
      },
      TimeoutSeconds: { optional: true, default: "100", private: false, description: null },
    },
  },
]);

const showBusy = (properties: Record<string, string>) => {
  server.use(
    busyCatalog,
    json("/globaladaptervaluessets", { result: [], totalCount: 0 }),
    json("/partners", { result: [], totalCount: 0 }),
  );
  return render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <AdapterConfig
        kind="handler"
        adapterId="NativeHttpHandler"
        properties={properties}
        onChange={() => {}}
        disabled={false}
      />
    </QueryClientProvider>,
  );
};

describe("AdapterConfig settings layout", () => {
  it("labels settings in words, keeps the key beside them, and folds optional ones into closed sections", async () => {
    showBusy({ Url: "https://host/orders" });

    // Required, always on screen, humanised, with its key kept quietly beside it.
    expect(await screen.findByLabelText("URL *")).toHaveValue("https://host/orders");
    expect(screen.getByText("Url")).toBeInTheDocument();

    // Nothing optional is set, so every section is closed and says so.
    const request = screen.getByRole("button", { name: /^Request/ });
    expect(request).toHaveAttribute("aria-expanded", "false");
    expect(request).toHaveTextContent("none set");
    expect(screen.getByRole("button", { name: /^Authentication/ })).toHaveAttribute("aria-expanded", "false");
    expect(screen.getByRole("button", { name: /^Advanced/ })).toHaveAttribute("aria-expanded", "false");
    expect(screen.queryByLabelText("Content type")).not.toBeInTheDocument();

    await userEvent.click(request);
    expect(screen.getByLabelText("Content type")).toBeInTheDocument();
    expect(screen.getByText("ContentType")).toBeInTheDocument();
  });

  it("opens a section that has a value, and names what is set in one that stays closed", async () => {
    showBusy({ Url: "https://host", Verb: "put", ContentType: "application/xml", ApiKey: "__private__" });

    expect(await screen.findByRole("button", { name: /^Request/ })).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByLabelText("Verb")).toHaveValue("put");
    const auth = screen.getByRole("button", { name: /^Authentication/ });
    expect(auth).toHaveAttribute("aria-expanded", "true");

    // Closed again by hand: the header now carries the summary, with the secret named, not shown.
    await userEvent.click(auth);
    expect(auth).toHaveTextContent("API key set");
    await userEvent.click(screen.getByRole("button", { name: /^Request/ }));
    expect(screen.getByRole("button", { name: /^Request/ })).toHaveTextContent("put, application/xml");
  });

  it("clamps long help to a couple of lines with a way to read the rest", async () => {
    showBusy({ Url: "https://host" });
    await userEvent.click(await screen.findByRole("button", { name: /^Authentication/ }));

    const more = screen.getByRole("button", { name: "more" });
    expect(more).toHaveAttribute("aria-expanded", "false");
    await userEvent.click(more);
    expect(screen.getByRole("button", { name: "less" })).toHaveAttribute("aria-expanded", "true");
  });
});
