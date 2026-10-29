import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { MoreActionsMenu, RunStateControl } from "../HeaderActions";

const states = [
  { value: "running", label: "Running", caption: "Running: accepts work." },
  { value: "paused", label: "Paused", caption: "Paused: holds work." },
  { value: "off", label: "Off", caption: "Off: drops work." },
] as const;

describe("RunStateControl", () => {
  it("captions the current state and asks for a move only when it is one", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(<RunStateControl label="Running state" options={[...states]} value="paused" onChange={onChange} />);

    expect(screen.getByText("Paused: holds work.")).toBeVisible();
    expect(screen.getByRole("radio", { name: "Paused" })).toBeChecked();

    await user.click(screen.getByRole("radio", { name: "Paused" }));
    expect(onChange).not.toHaveBeenCalled();

    await user.click(screen.getByRole("radio", { name: "Off" }));
    expect(onChange).toHaveBeenCalledWith("off");
  });
});

describe("MoreActionsMenu", () => {
  it("opens from the keyboard and runs the item picked", async () => {
    const user = userEvent.setup();
    const duplicate = vi.fn();
    const remove = vi.fn();
    render(
      <MoreActionsMenu
        actions={[
          { label: "Delete", danger: true, onSelect: remove },
          { label: "Duplicate", onSelect: duplicate },
        ]}
      />,
    );

    await user.tab();
    expect(screen.getByRole("button", { name: "More actions" })).toHaveFocus();
    await user.keyboard("{Enter}");

    // Delete comes last whatever order it was given in, apart from the rest.
    const items = await screen.findAllByRole("menuitem");
    expect(items.map((i) => i.textContent)).toEqual(["Duplicate", "Delete"]);
    expect(screen.getByRole("separator")).toBeInTheDocument();

    await user.click(screen.getByRole("menuitem", { name: "Delete" }));
    expect(remove).toHaveBeenCalledOnce();
    expect(duplicate).not.toHaveBeenCalled();
  });

  it("is left out when every item is hidden", () => {
    render(<MoreActionsMenu actions={[{ label: "Delete", danger: true, onSelect: () => {}, hidden: true }]} />);
    expect(screen.queryByRole("button", { name: "More actions" })).not.toBeInTheDocument();
  });
});
