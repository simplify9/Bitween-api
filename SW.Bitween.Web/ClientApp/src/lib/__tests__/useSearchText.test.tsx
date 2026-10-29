import { act, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";
import { describe, expect, it, vi } from "vitest";
import { useSearchText } from "../useSearchText";

function Box({ onRun }: { onRun: (text: string) => void }) {
  const [search, setSearch] = useState("");
  const [text, setText] = useSearchText(search, (t) => (onRun(t), setSearch(t)));
  return (
    <>
      <input aria-label="Search" value={text} onChange={(e) => setText(e.target.value)} />
      <button onClick={() => setSearch("")}>Clear filters</button>
    </>
  );
}

describe("useSearchText", () => {
  it("runs the search once typing pauses, not on every key", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    try {
      const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
      const onRun = vi.fn();
      render(<Box onRun={onRun} />);

      await user.type(screen.getByRole("textbox", { name: "Search" }), "orders");
      expect(screen.getByRole("textbox", { name: "Search" })).toHaveValue("orders");
      act(() => vi.advanceTimersByTime(300));
      expect(onRun).toHaveBeenCalledTimes(1);
      expect(onRun).toHaveBeenCalledWith("orders");

      // Changed from outside: the box follows.
      await user.click(screen.getByRole("button", { name: "Clear filters" }));
      expect(screen.getByRole("textbox", { name: "Search" })).toHaveValue("");
      act(() => vi.advanceTimersByTime(300));
      expect(onRun).toHaveBeenCalledTimes(1);
    } finally {
      vi.useRealTimers();
    }
  });
});
