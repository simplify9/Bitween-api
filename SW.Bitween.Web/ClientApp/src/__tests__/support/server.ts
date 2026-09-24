import { setupServer } from "msw/node";

/**
 * The network every component test runs against. It starts empty and refuses anything it wasn't
 * told about, so a page asking for more than its test expected fails loudly rather than rendering
 * an error state that happens to satisfy an assertion.
 */
export const server = setupServer();
