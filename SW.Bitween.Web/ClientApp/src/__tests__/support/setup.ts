import "@testing-library/jest-dom/vitest";
import { cleanup } from "@testing-library/react";
import { afterAll, afterEach, beforeAll } from "vitest";
import { resetAppConfig } from "../../api/http/appConfig";
import { server } from "./server";

/** Requests the page made that no handler answered, collected so the test can fail on them. */
const unanswered: string[] = [];

beforeAll(() => {
  // Printing alone isn't enough: vitest hides a passing test's output, and the page just renders
  // its error state — which can satisfy an assertion as easily as the real thing would.
  server.listen({
    onUnhandledRequest: (request, print) => {
      const url = new URL(request.url);
      unanswered.push(`${request.method} ${url.pathname}${url.search}`);
      print.error();
    },
  });

  // The API client asks for "/api/…", which a browser resolves against the page. Node's fetch has
  // no page, so do what the browser would. Installed after listen() so it wraps the fetch the mock
  // server intercepts, rather than the one underneath it.
  const intercepted = globalThis.fetch;
  globalThis.fetch = (input, init) =>
    intercepted(
      typeof input === "string" && input.startsWith("/") ? new URL(input, window.location.origin) : input,
      init,
    );
});

afterEach(() => {
  cleanup();
  server.resetHandlers();
  localStorage.clear();
  // Unsaved drafts (the settings page's, for one) live here, and would leak into the next test.
  sessionStorage.clear();
  // Fetched once per page load and cached for the life of the module; each test is a new page.
  resetAppConfig();

  // Last, so a failure here still leaves the next test a clean page.
  const leaked = unanswered.splice(0);
  if (leaked.length) throw new Error(`The page asked for something no handler answers: ${leaked.join(", ")}`);
});

afterAll(() => server.close());
