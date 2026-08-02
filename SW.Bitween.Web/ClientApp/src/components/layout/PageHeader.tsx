import { useState, type ReactNode } from "react";
import { CircleHelp, X } from "lucide-react";

/**
 * Every page opens with the same frame: what this page is, one line of
 * plain-words help, and the page's primary actions — so clients can
 * find their feet without a demo.
 */
export function PageHeader({
  title,
  description,
  actions,
  help,
}: {
  title: string;
  description?: string;
  actions?: ReactNode;
  /** Optional expandable "how this works" explainer. */
  help?: { title: string; body: ReactNode };
}) {
  const [helpOpen, setHelpOpen] = useState(false);

  return (
    <div className="mb-3.5">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="flex items-center gap-2 text-[20px] font-semibold tracking-tight text-ink-900">
            {title}
            {help && (
              <button
                onClick={() => setHelpOpen((o) => !o)}
                aria-expanded={helpOpen}
                aria-label={`How ${title.toLowerCase()} works`}
                className="rounded-full p-0.5 text-ink-300 hover:text-crimson-600"
              >
                <CircleHelp className="size-4.5" />
              </button>
            )}
          </h1>
          {description && <p className="mt-1 max-w-2xl text-sm text-ink-500">{description}</p>}
        </div>
        {actions && <div className="flex shrink-0 items-center gap-2">{actions}</div>}
      </div>

      {help && helpOpen && (
        <div className="relative mt-4 rounded-xl border border-crimson-100 bg-crimson-50/60 px-4 py-3.5">
          <button
            onClick={() => setHelpOpen(false)}
            aria-label="Close help"
            className="absolute top-2.5 right-2.5 rounded-md p-1 text-ink-400 hover:text-ink-700"
          >
            <X className="size-4" />
          </button>
          <h2 className="text-sm font-semibold text-ink-900">{help.title}</h2>
          <div className="mt-1.5 max-w-3xl space-y-2 text-sm leading-relaxed text-ink-600">
            {help.body}
          </div>
        </div>
      )}
    </div>
  );
}
