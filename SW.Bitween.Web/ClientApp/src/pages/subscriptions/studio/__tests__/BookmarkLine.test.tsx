import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { apiPath } from "../../../../__tests__/support/renderApp";
import { server } from "../../../../__tests__/support/server";
import { SessionContext } from "../../../../auth/useSession";
import { BookmarkLine } from "../ReceiveAttemptsPanel";

function mount(permissions: string[] = ["subscriptions.operate"]) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const session = { can: (p: string) => permissions.includes(p) } as never;
  render(
    <QueryClientProvider client={client}>
      <SessionContext.Provider value={session}>
        <BookmarkLine subscriptionId={7} />
      </SessionContext.Provider>
    </QueryClientProvider>,
  );
  return userEvent.setup();
}

describe("a receiver's bookmark", () => {
  it("says where the next run starts, and forgets it when asked", async () => {
    let value: string | null = "2026-10-05T10:00:00Z";
    const forgotten: number[] = [];
    server.use(
      http.get(apiPath("/subscriptions/bookmark"), () => HttpResponse.json({ value, updatedOn: new Date().toISOString() })),
      http.post(apiPath("/subscriptions/7/forgetbookmark"), () => {
        forgotten.push(7);
        value = null;
        return HttpResponse.json({ forgotten: true });
      }),
    );
    const user = mount();

    expect(await screen.findByText("2026-10-05T10:00:00Z")).toBeVisible();
    await user.click(screen.getByRole("button", { name: "Forget bookmark" }));
    // The dialog's own button, which says what it does again.
    const buttons = await screen.findAllByRole("button", { name: "Forget bookmark" });
    await user.click(buttons[buttons.length - 1]);
    await waitFor(() => expect(forgotten).toEqual([7]));
    await waitFor(() => expect(screen.queryByText("2026-10-05T10:00:00Z")).not.toBeInTheDocument());
  });

  it("shows nothing when there is no bookmark, and no button without the permission", async () => {
    server.use(http.get(apiPath("/subscriptions/bookmark"), () => HttpResponse.json({ value: "42", updatedOn: null })));
    mount([]);
    expect(await screen.findByText("42")).toBeVisible();
    expect(screen.queryByRole("button", { name: "Forget bookmark" })).not.toBeInTheDocument();
  });
});
