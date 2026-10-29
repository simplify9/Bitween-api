import { useState } from "react";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { Dialog } from "../overlays";
import { Table } from "../Table";

function Opener() {
  const [open, setOpen] = useState(false);
  return (
    <>
      <button onClick={() => setOpen(true)}>Open</button>
      {open && (
        <Dialog title="Rename" onClose={() => setOpen(false)}>
          <input aria-label="Name" />
          <button>Save</button>
        </Dialog>
      )}
    </>
  );
}

describe("Dialog and the keyboard", () => {
  it("takes focus when it opens, keeps Tab inside, and hands focus back when it closes", async () => {
    const user = userEvent.setup();
    render(<Opener />);
    await user.click(screen.getByRole("button", { name: "Open" }));

    const dialog = screen.getByRole("dialog", { name: "Rename" });
    expect(dialog).toHaveFocus();

    await user.tab();
    expect(screen.getByRole("button", { name: "Close" })).toHaveFocus();
    await user.tab();
    expect(screen.getByRole("textbox", { name: "Name" })).toHaveFocus();
    await user.tab();
    expect(screen.getByRole("button", { name: "Save" })).toHaveFocus();
    // Past the last control, round to the first rather than out to the page behind.
    await user.tab();
    expect(screen.getByRole("button", { name: "Close" })).toHaveFocus();
    await user.tab({ shift: true });
    expect(screen.getByRole("button", { name: "Save" })).toHaveFocus();

    await user.keyboard("{Escape}");
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Open" })).toHaveFocus();
  });

  it("leaves focus on a field that asked for it", async () => {
    render(
      <Dialog title="New" onClose={() => {}}>
        <input aria-label="Name" autoFocus />
      </Dialog>,
    );
    expect(screen.getByRole("textbox", { name: "Name" })).toHaveFocus();
  });
});

describe("A table row that opens something", () => {
  it("opens from the keyboard, but leaves keys pressed on a control in the row alone", async () => {
    const user = userEvent.setup();
    const opened = vi.fn();
    const pressed = vi.fn();
    render(
      <Table
        rows={[{ id: 1, name: "Orders" }]}
        rowKey={(r) => r.id}
        onRowClick={(r) => opened(r.name)}
        columns={[
          { header: "Name", cell: (r) => r.name },
          {
            header: "",
            cell: () => <button onClick={(e) => (e.stopPropagation(), pressed())}>Pause</button>,
          },
        ]}
      />,
    );

    await user.tab();
    const row = screen.getByRole("row", { name: /Orders/ });
    expect(row).toHaveFocus();
    await user.keyboard("{Enter}");
    expect(opened).toHaveBeenCalledWith("Orders");

    await user.tab();
    expect(screen.getByRole("button", { name: "Pause" })).toHaveFocus();
    await user.keyboard("{Enter}");
    expect(pressed).toHaveBeenCalledOnce();
    expect(opened).toHaveBeenCalledOnce();
  });
});
