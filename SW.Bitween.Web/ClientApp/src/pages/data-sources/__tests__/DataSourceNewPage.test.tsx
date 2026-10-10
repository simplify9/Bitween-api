import { screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";

/** Creating a data source asks for what its provider can't connect without. */
const POSTGRES = {
  adapterId: "bitween.db.postgresql",
  label: "PostgreSQL",
  kind: "Relational",
  description: "A PostgreSQL database.",
  settings: [
    { name: "Host", type: "string", hint: "Host name or IP.", default: null, allowedValues: null, secret: false, required: true },
    { name: "Port", type: "number", hint: null, default: "5432", allowedValues: null, secret: false, required: false },
    { name: "Database", type: "string", hint: null, default: null, allowedValues: null, secret: false, required: true },
    { name: "Password", type: "string", hint: null, default: null, allowedValues: null, secret: true, required: true },
  ],
};

describe("a new data source", () => {
  it("asks for the required settings, and creates it with them and the defaults", async () => {
    let created: Record<string, unknown> | null = null;
    const { user } = renderApp("/data-sources/new", {
      handlers: [
        http.get(apiPath("/datasources/Providers"), () => HttpResponse.json([POSTGRES])),
        http.post(apiPath("/datasources"), async ({ request }) => {
          created = (await request.json()) as Record<string, unknown>;
          return HttpResponse.json(9);
        }),
        http.get(apiPath("/datasources/9"), () => HttpResponse.json({})),
      ],
    });

    expect(await screen.findByText("A connection Bitween keeps open to a database outside it.", undefined, { timeout: 5000 })).toBeVisible();
    await user.type(screen.getByLabelText("Name"), "ERP database");
    const create = screen.getByRole("button", { name: "Create" });
    expect(create).toBeDisabled();
    expect(screen.getByText("Still needs Host, Database, Password.")).toBeVisible();

    await user.type(screen.getByLabelText("Host"), "db.local");
    await user.type(screen.getByLabelText("Database"), "erp");
    await user.type(screen.getByLabelText("Password"), "s3cret");
    expect(create).toBeEnabled();
    await user.click(create);

    await waitFor(() => expect(created).not.toBeNull());
    expect(created!.properties).toEqual({ Host: "db.local", Port: "5432", Database: "erp", Password: "s3cret" });
  });
});
