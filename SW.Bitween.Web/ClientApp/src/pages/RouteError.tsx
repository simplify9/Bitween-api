import { Link, isRouteErrorResponse, useRouteError } from "react-router";
import { TriangleAlert } from "lucide-react";
import { Button } from "../components/ui/basics";

/**
 * What a page that crashed shows, in place of react-router's developer screen. The app around it
 * keeps working; reloading usually clears it, and the details are there for a bug report.
 */
export function RouteError() {
  const error = useRouteError();
  const detail = isRouteErrorResponse(error)
    ? `${error.status} ${error.statusText}`
    : error instanceof Error
      ? `${error.name}: ${error.message}`
      : String(error);

  return (
    <div role="alert" className="flex flex-col items-center justify-center gap-3 px-6 py-24 text-center">
      <span className="flex size-12 items-center justify-center rounded-full bg-danger-50 text-danger-600">
        <TriangleAlert className="size-5" />
      </span>
      <h1 className="text-lg font-semibold text-ink-900">Something went wrong on this page</h1>
      <p className="max-w-md text-sm text-ink-600">
        Nothing was saved or lost by this. Reloading usually clears it; if it keeps happening, send the
        details below to whoever supports your Bitween.
      </p>
      <div className="flex gap-2">
        <Button variant="primary" onClick={() => window.location.reload()}>
          Reload
        </Button>
        <Link to="/" className="inline-flex items-center rounded-lg border border-ink-200 px-3 text-sm text-ink-800 hover:bg-ink-50">
          Go to the start page
        </Link>
      </div>
      <details className="mt-2 max-w-xl text-left text-[12px] text-ink-600">
        <summary className="cursor-pointer">Details</summary>
        <pre className="mt-2 overflow-x-auto rounded-md bg-ink-50 p-3 whitespace-pre-wrap">{detail}</pre>
      </details>
    </div>
  );
}
