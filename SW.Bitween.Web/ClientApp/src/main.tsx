import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { MutationCache, QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { RouterProvider } from "react-router";
import { shouldRetryQuery } from "./api/types";
import { applyQueryDefaults, keys } from "./api/queryKeys";
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

// Any successful mutation may have written an audit row, so the trail is refreshed here rather
// than by each of the app's save handlers remembering to. Without it a History card goes on
// showing the state from before the save made on the very same page, until a manual reload.
const mutationCache = new MutationCache({
  onSuccess: () => {
    void queryClient.invalidateQueries({ queryKey: keys.audit.all });
  },
});

const queryClient = new QueryClient({
  mutationCache,
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
      retry: shouldRetryQuery,
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
