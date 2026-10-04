import { useState } from "react";
import { fireEvent, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { KeyValueEditor, type KvRow } from "../KeyValueEditor";

/** Promoted properties, the editor whose order means something. */
function Ordered({ initial }: { initial: KvRow[] }) {
  const [rows, setRows] = useState(initial);
  return (
    <KeyValueEditor
      rows={rows}
      onChange={setRows}
      keyLabel="Friendly name"
      valueLabel="JSON path"
      editable
      emptyText="None"
      reorderable={{ first: { label: "Main", title: "Shown first." } }}
    />
  );
}

const ROWS: KvRow[] = [
  { key: "Customer", value: "$.customer" },
  { key: "OrderNumber", value: "$.order" },
  { key: "City", value: "$.city" },
];

const names = () =>
  screen.getAllByRole("textbox", { name: /^Friendly name/ }).map((box) => (box as HTMLInputElement).value);

describe("a reorderable key-value editor", () => {
  it("moves a row with the arrow keys and keeps focus on it", async () => {
    const user = userEvent.setup();
    render(<Ordered initial={ROWS} />);

    const handle = screen.getByRole("button", { name: /Move OrderNumber/ });
    handle.focus();
    await user.keyboard("{ArrowUp}");

    expect(names()).toEqual(["OrderNumber", "Customer", "City"]);
    // Focus followed the row that moved, so pressing again keeps moving the same property.
    expect(screen.getByRole("button", { name: /Move OrderNumber/ })).toHaveFocus();
    // The marker belongs to whichever row is first now.
    expect(screen.getByText("Main").closest("td")!.querySelector("input")).toHaveValue("OrderNumber");
  });

  it("moves a row dragged by its handle onto another", () => {
    render(<Ordered initial={ROWS} />);
    const data = { effectAllowed: "", dropEffect: "", setData: () => {}, setDragImage: () => {} };

    fireEvent.dragStart(screen.getByRole("button", { name: /Move City/ }), { dataTransfer: data });
    const firstRow = screen.getByRole("button", { name: /Move Customer/ }).closest("tr")!;
    fireEvent.dragOver(firstRow, { dataTransfer: data });
    fireEvent.drop(firstRow, { dataTransfer: data });

    expect(names()).toEqual(["City", "Customer", "OrderNumber"]);
  });

  it("offers no handles when there's nothing to order", () => {
    render(<Ordered initial={[ROWS[0]]} />);
    expect(screen.queryByRole("button", { name: /^Move/ })).not.toBeInTheDocument();
  });
});
