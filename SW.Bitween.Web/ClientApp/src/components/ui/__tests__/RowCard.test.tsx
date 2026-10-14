import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { Table } from "../Table";
import { onAPhone } from "../../../__tests__/support/phone";

interface Lane {
  id: number;
  name: string;
  queue: string;
  dead: number;
}

const rows: Lane[] = [{ id: 1, name: "Orders", queue: "v3.prod.bitween.xchangeservice.orders.retry", dead: 4 }];

const table = (onRowClick?: (r: Lane) => void) => (
  <Table
    rows={rows}
    rowKey={(r) => r.id}
    onRowClick={onRowClick}
    columns={[
      { header: "Name", cell: (r) => r.name },
      { header: "Dead", cell: (r) => r.dead },
    ]}
    card={(r) => ({ title: r.name, technical: r.queue, facts: [{ label: "Dead", value: r.dead, tone: "danger" }] })}
    footer={<p>Page 1 of 1</p>}
  />
);

describe("a table on a desk", () => {
  it("keeps its columns when the screen is wide", () => {
    render(table());
    expect(screen.getByRole("table")).toBeInTheDocument();
    expect(screen.getByRole("columnheader", { name: "Dead" })).toBeInTheDocument();
  });
});

describe("a table on a phone", () => {
  onAPhone();

  it("turns each row into a card with its full technical name and numbers", () => {
    render(table());
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
    const card = within(screen.getByRole("listitem"));
    expect(card.getByText("Orders")).toBeInTheDocument();
    expect(card.getByText("v3.prod.bitween.xchangeservice.orders.retry")).toBeInTheDocument();
    expect(card.getByText("Dead").nextSibling).toHaveTextContent("4");
    // The footer (pagination on a real list) comes along once, under the cards.
    expect(screen.getByText("Page 1 of 1")).toBeInTheDocument();
  });

  it("opens the row from its card", async () => {
    const open = vi.fn();
    render(table(open));
    await userEvent.click(screen.getByText("Orders"));
    expect(open).toHaveBeenCalledWith(rows[0]);
  });
});
