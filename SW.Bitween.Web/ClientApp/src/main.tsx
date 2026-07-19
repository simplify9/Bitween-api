import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { RouterProvider } from "react-router";
import { SessionProvider } from "./auth/SessionContext";
import { router } from "./router";
import "./index.css";

// Reaching the app outside its base path (e.g. "/" when hosted under
// /bitween) can't be routed — bounce to the base instead of a blank page.
const base = import.meta.env.BASE_URL;
if (base !== "/" && !window.location.pathname.startsWith(base.replace(/\/$/, ""))) {
  window.location.replace(base);
}

const queryClient = new QueryClient({
  defaultOptions: {
    queries: { staleTime: 10_000, refetchOnWindowFocus: false },
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
