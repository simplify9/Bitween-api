import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { createMemoryRouter, RouterProvider } from "react-router";
import { describe, expect, it } from "vitest";
import { ListBody } from "../../components/ui/ListBody";
import { useListParams } from "../listParams";

function List() {
  const { q, offset, params, set, searchText, setSearchText } = useListParams();
  return (
    <>
      <input aria-label="Search" value={searchText} onChange={(e) => setSearchText(e.target.value)} />
      <button onClick={() => set("status", "active")}>Active only</button>
      <button onClick={() => set("offset", "25")}>Next page</button>
      <p>
        q={q} offset={offset} status={params.get("status") ?? ""}
      </p>
    </>
  );
}

const mount = (url: string) => {
  const router = createMemoryRouter([{ path: "/things", element: <List /> }], { initialEntries: ["/elsewhere", url] });
  render(<RouterProvider router={router} />);
  return { router, user: userEvent.setup() };
};

describe("useListParams", () => {
  it("goes back to the first page when a filter changes, and Back undoes the filter", async () => {
    const { router, user } = mount("/things?offset=50");
    await user.click(screen.getByRole("button", { name: "Active only" }));
    expect(screen.getByText("q= offset=0 status=active")).toBeVisible();

    await router.navigate(-1);
    await waitFor(() => expect(screen.getByText("q= offset=50 status=")).toBeVisible());
  });

  it("searches once typing pauses, without a history entry per search", async () => {
    const { router, user } = mount("/things");
    await user.click(screen.getByRole("button", { name: "Next page" }));
    await user.type(screen.getByRole("textbox", { name: "Search" }), "acme");
    await waitFor(() => expect(screen.getByText("q=acme offset=0 status=")).toBeVisible());

    // Back leaves the search's entry for the one before it: the first page, unsearched.
    await router.navigate(-1);
    await waitFor(() => expect(screen.getByText("q= offset=0 status=")).toBeVisible());
  });
});

describe("ListBody", () => {
  const query = { isPending: false, isError: false, error: null, refetch: async () => ({}) as never };
  const empty = { icon: null, title: "No things yet", body: "Make one." };

  it("tells an empty list from a search that found nothing", () => {
    const { rerender } = render(
      <ListBody query={query} rows={[]} what="things" filtered={false} empty={empty}>
        {() => null}
      </ListBody>,
    );
    expect(screen.getByText("No things yet")).toBeVisible();

    rerender(
      <ListBody query={query} rows={[]} what="things" filtered empty={empty}>
        {() => null}
      </ListBody>,
    );
    expect(screen.getByText("Nothing matches")).toBeVisible();
  });
});
