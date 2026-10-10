import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { Field, Select, TextInput } from "../forms";

describe("Field", () => {
  it("names its control, and describes it with the hint", () => {
    render(
      <Field label="Name" hint="Shown to partners.">
        <TextInput />
      </Field>,
    );
    const input = screen.getByRole("textbox", { name: "Name" });
    expect(input).toHaveAccessibleDescription("Shown to partners.");
    expect(input).not.toHaveAttribute("aria-invalid");
  });

  it("marks its control invalid, described by the error in place of the hint", () => {
    render(
      <Field label="Runs on" hint="Any node." error="Pick a node that exists.">
        <Select options={[{ value: "", label: "Any" }]} />
      </Field>,
    );
    const select = screen.getByRole("combobox", { name: "Runs on" });
    expect(select).toBeInvalid();
    expect(select).toHaveAccessibleDescription("Pick a node that exists.");
  });

  it("leaves ids to a Field that gives its own", () => {
    render(
      <Field label="From" htmlFor="from">
        <TextInput id="from" />
        <TextInput aria-label="To" />
      </Field>,
    );
    expect(screen.getByRole("textbox", { name: "From" })).toHaveAttribute("id", "from");
    expect(screen.getByRole("textbox", { name: "To" })).not.toHaveAttribute("id");
  });
});
