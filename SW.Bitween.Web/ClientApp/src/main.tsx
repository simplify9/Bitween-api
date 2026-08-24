import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { RouterProvider } from "react-router";
import { NotWiredError } from "./api/types";
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
      staleTime: 10_000,
      refetchOnWindowFocus: false,
      // NotWiredError is permanent (the method will reject every time, with no
      // network round-trip) — retrying it just stalls "Loading…" states for the
      // default ~7s of exponential backoff for nothing.
      retry: (failureCount, error) => !(error instanceof NotWiredError) && failureCount < 3,
    },
  },
});

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <SessionProvider>
        <RouterProvider router={router} />
      </SessionProvider>
    </QueryClientProvider>
  </StrictMode>,
);
