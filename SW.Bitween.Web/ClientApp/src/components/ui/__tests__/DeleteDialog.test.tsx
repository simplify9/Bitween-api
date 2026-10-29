import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { DeleteDialog, counted } from "../DeleteDialog";

const open = (props: Partial<Parameters<typeof DeleteDialog>[0]> = {}) => {
  const onConfirm = vi.fn(async () => {});
  render(
    <DeleteDialog
      title="Delete this gateway?"
      name="Orders in"
      body="It will be gone for good."
      confirmLabel="Delete gateway"
      onConfirm={onConfirm}
      onClose={() => {}}
      {...props}
    />,
  );
  return { onConfirm, confirm: () => screen.getByRole("button", { name: "Delete gateway" }) };
};

describe("DeleteDialog", () => {
  it("is a one-click confirm when nothing is affected", async () => {
    const user = userEvent.setup();
    const { onConfirm, confirm } = open({ impact: [counted(0, "route")] });

    expect(screen.queryByRole("textbox")).not.toBeInTheDocument();
    await user.click(confirm());
    await waitFor(() => expect(onConfirm).toHaveBeenCalledOnce());
  });

  it("lists what it takes with it and wants the name typed first", async () => {
    const user = userEvent.setup();
    const { onConfirm, confirm } = open({ impact: [counted(6, "route"), counted(0, "attachment")] });

    expect(screen.getByText("6 routes")).toBeVisible();
    expect(screen.queryByText("0 attachments")).not.toBeInTheDocument();
    expect(confirm()).toBeDisabled();

    await user.type(screen.getByRole("textbox", { name: /Type Orders in to confirm/ }), "Orders");
    expect(confirm()).toBeDisabled();
    await user.type(screen.getByRole("textbox"), " in");
    expect(confirm()).toBeEnabled();

    await user.click(confirm());
    await waitFor(() => expect(onConfirm).toHaveBeenCalledOnce());
  });

  it("names what holds it without asking for the name, since the server refuses anyway", () => {
    const { confirm } = open({ blockers: [counted(1, "subscription")], impact: [counted(3, "route")] });

    expect(screen.getByText("1 subscription")).toBeVisible();
    expect(screen.getByText(/refused until they are moved off it/)).toBeVisible();
    expect(screen.queryByRole("textbox")).not.toBeInTheDocument();
    expect(confirm()).toBeEnabled();
  });
});
