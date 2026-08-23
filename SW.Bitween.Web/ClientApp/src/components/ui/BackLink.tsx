import { Link, useLocation, useNavigate } from "react-router";
import { ArrowLeft } from "lucide-react";

/**
 * The way back out of a detail page.
 *
 * These were all fixed `Link`s to a list page, which is only right when the list is
 * where you came from. `subscriptions/:id` is reached from Scheduled jobs, Exchanges,
 * an API gateway's page, the retry usage panel and three places on the dashboard — and
 * from every one of them "← Integrations" landed you somewhere you had never been.
 *
 * So it steps back when there is somewhere to step back to, and falls back to `to` when
 * there isn't: a pasted link, a new tab, a refresh. The label follows the behaviour
 * rather than the other way round — naming a destination it wasn't going to is exactly
 * how the old one misled people.
 *
 * Signing in can't be what's behind you: `Login` navigates with `replace`, so that entry
 * is already gone.
 */
export function BackLink({
  to,
  label,
  className = "mb-4",
}: {
  to: string;
  label: string;
  /** Layout only — one caller sits in a toolbar rather than above a page title. */
  className?: string;
}) {
  const navigate = useNavigate();
  // Subscribed to purely so a navigation re-renders this and the index below is re-read.
  useLocation();

  // The history index, which React Router keeps in `history.state`: it counts pushes and
  // is untouched by replaces. `location.key` looked like the same signal and isn't — a
  // replace mints a fresh key, so a page that syncs a query param on mount (the bus
  // gateway selecting its first route) looked like it had somewhere to go back to on a
  // cold load. Reading it during render is fine because `location` above re-renders us
  // on every navigation.
  const historyIndex = (window.history.state as { idx?: number } | null)?.idx ?? 0;
  const cameFromInsideTheApp = historyIndex > 0;

  const classes = `${className} inline-flex items-center gap-1 text-[13px] font-medium text-ink-500 hover:text-ink-800`;

  if (cameFromInsideTheApp)
    return (
      <button type="button" onClick={() => navigate(-1)} className={`${classes} cursor-pointer`}>
        <ArrowLeft className="size-3.5" /> Back
      </button>
    );

  return (
    <Link to={to} className={classes}>
      <ArrowLeft className="size-3.5" /> {label}
    </Link>
  );
}
