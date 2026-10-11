import { describe, expect, it } from "vitest";

import { draftNameFor, draftStatementFor, groupBySchema, qualify, type DbObject } from "../schema";

const object = (over: Partial<DbObject> = {}): DbObject => ({
  schema: "sales",
  name: "orders",
  type: "table",
  rowCount: null,
  columns: [],
  parameters: [],
  comment: null,
  ...over,
});

describe("groupBySchema", () => {
  it("groups without reordering what the adapter returned", () => {
    // The catalog query orders by schema then name; re-sorting here would fight it and put a
    // page's rows in a different order from the page before it.
    const grouped = groupBySchema([
      object({ schema: "sales", name: "orders" }),
      object({ schema: "sales", name: "customers" }),
      object({ schema: "audit", name: "log" }),
    ]);

    expect(grouped.map((g) => g.schema)).toEqual(["sales", "audit"]);
    expect(grouped[0].objects.map((o) => o.name)).toEqual(["orders", "customers"]);
  });
});

describe("qualify", () => {
  it("leaves a plain identifier alone", () => {
    expect(qualify("sales", "orders")).toBe("sales.orders");
  });

  it("quotes anything that would not resolve unquoted", () => {
    // PostgreSQL folds an unquoted name to lower case, so an unquoted mixed-case name finds
    // nothing - generating SQL the user cannot run is worse than generating none.
    expect(qualify("sales", "Orders")).toBe('sales."Orders"');
    expect(qualify("my schema", "order-line")).toBe('"my schema"."order-line"');
  });
});

describe("draftStatementFor", () => {
  it("names the columns it knows about, and limits the rows", () => {
    // The row limit is not politeness: the first thing anyone does with a new statement is run it
    // against a table whose size they have not checked.
    const sql = draftStatementFor(
      object({
        columns: [
          { name: "id", dbType: "integer", clrType: "Int32", nullable: false, primaryKey: true, generated: true, ordinal: 1 },
          { name: "total", dbType: "numeric", clrType: "Decimal", nullable: true, primaryKey: false, generated: false, ordinal: 2 },
        ],
      }),
    );

    expect(sql).toContain("select id, total");
    expect(sql).toContain("from sales.orders");
    expect(sql).toContain("limit 100");
  });

  it("falls back to a star when the columns have not been read yet", () => {
    // The row can be used before it is expanded, and a statement that names no columns is still
    // a working starting point.
    expect(draftStatementFor(object())).toContain("select *");
  });

  it("names a procedure rather than writing a CALL for it", () => {
    const sql = draftStatementFor(
      object({
        type: "procedure",
        name: "release_order",
        parameters: [
          { name: "order_id", dbType: "integer", direction: "In", ordinal: 1 },
          { name: "released", dbType: "boolean", direction: "Out", ordinal: 2 },
        ],
      }),
    );

    // A statement meant for Call holds the procedure's NAME, not SQL - that is what
    // CommandType.StoredProcedure takes, and on Oracle it is the only form that works. Drafting
    // "call sales.release_order(@order_id)" made a statement whose procedure name was that entire
    // string, which fails on first use and nowhere near here.
    expect(sql).toBe("sales.release_order");
  });

  it("writes the placeholder and the row limit the engine actually takes", () => {
    const table = object({
      columns: [
        { name: "id", dbType: "integer", clrType: "Int32", nullable: false, primaryKey: true, generated: true, ordinal: 1 },
      ],
    });

    // Oracle: fetch first, and : for a bind.
    expect(draftStatementFor(table, { parameterPrefix: ":", limitStyle: "fetchFirst" }))
      .toContain("fetch first 100 rows only");

    // SQL Server: TOP, and it goes BEFORE the columns rather than after the query.
    expect(draftStatementFor(table, { parameterPrefix: "@", limitStyle: "top" }))
      .toContain("select top 100 id");

    const fn = object({
      type: "function",
      name: "orders_for",
      parameters: [{ name: "code", dbType: "varchar2", direction: "In", ordinal: 1 }],
    });

    expect(draftStatementFor(fn, { parameterPrefix: ":", limitStyle: "fetchFirst" }))
      .toBe("select * from sales.orders_for(:code)");
  });

  it("reads the next value of a sequence the way each engine spells it", () => {
    const seq = object({ type: "sequence", name: "order_seq" });

    expect(draftStatementFor(seq, { parameterPrefix: "@", limitStyle: "limit" }))
      .toBe("select nextval('sales.order_seq')");
    expect(draftStatementFor(seq, { parameterPrefix: "@", limitStyle: "top" }))
      .toBe("select next value for sales.order_seq");
    expect(draftStatementFor(seq, { parameterPrefix: ":", limitStyle: "fetchFirst" }))
      .toBe("select sales.order_seq.nextval from dual");
  });

  it("selects from a set-returning function", () => {
    const sql = draftStatementFor(
      object({
        type: "function",
        name: "orders_for_customer",
        parameters: [{ name: "cid", dbType: "integer", direction: "In", ordinal: 1 }],
      }),
    );

    expect(sql).toBe("select * from sales.orders_for_customer(@cid)");
  });


});

describe("draftNameFor", () => {
  it("makes a name that reads as what it does", () => {
    expect(draftNameFor(object({ name: "order_line" }))).toBe("readOrderLine");
    expect(draftNameFor(object({ type: "procedure", name: "release_order" }))).toBe(
      "callReleaseOrder",
    );
  });
});
