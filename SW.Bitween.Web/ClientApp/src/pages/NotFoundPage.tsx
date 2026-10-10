import { Link } from "react-router";

export function NotFoundPage() {
  return (
    <div className="flex flex-col items-center justify-center gap-3 px-6 py-24 text-center">
      <p className="font-mono text-sm text-ink-500">404</p>
      <h1 className="text-lg font-semibold tracking-tight text-ink-900">This page doesn't exist</h1>
      <Link to="/" className="text-[13px] font-medium text-crimson-700 hover:underline">
        Go to your home page
      </Link>
    </div>
  );
}
