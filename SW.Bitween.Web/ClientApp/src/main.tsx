import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { RouterProvider } from "react-router";
import { NotWiredError } from "./api/types";
import { applyQueryDefaults } from "./api/queryKeys";
import { SessionProvider } from "./auth/SessionContext";
import { router } from "./router";
import "./index.css";

// Reaching the app outside its base path can't be routed — bounce to the base
// instead of a blank page. Inert while the app is served from the root, but
// keeps working if it is ever mounted under a prefix again.
const base = import.meta.env.BASE_URL;
if (base !== "/" && !window.location.pathname.startsWith(base.replace(/\/$/, ""))) {
  window.location.replace(base);
}

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      // A floor, not the policy: per-entity windows are registered by applyQueryDefaults below and
      // override this. It only governs a query whose key isn't in the catalog, where the safe
      // answer is "refetch on mount, but don't do it twice in ten seconds".
      staleTime: 10_000,
      // Kept long enough that going back to a page you were just on renders from cache instead of
      // re-fetching. Was the 5-minute default, which is shorter than a train of thought.
      gcTime: 30 * 60_000,
      refetchOnWindowFocus: false,
      // NotWiredError is permanent (the method will reject every time, with no
      // network round-trip) — retrying it just stalls "Loading…" states for the
      // default ~7s of exponential backoff for nothing.
      retry: (failureCount, error) => !(error instanceof NotWiredError) && failureCount < 3,
    },
  },
});

applyQueryDefaults(queryClient);

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <SessionProvider>
        <RouterProvider router={router} />
      </SessionProvider>
    </QueryClientProvider>
  </StrictMode>,
);
