import type { ReactNode } from "react";
import { useBranding } from "../../lib/branding";

/** The connection motif: systems as nodes, Bitween as the path between them. */
function FlowLines() {
  return (
    <svg
      viewBox="0 0 480 480"
      aria-hidden
      className="pointer-events-none absolute inset-x-0 bottom-0 w-full opacity-60"
    >
      <g fill="none" strokeWidth="1.25">
        <path d="M-20 380 C 120 340, 180 440, 300 400 S 480 320, 520 360" stroke="#4d4341" />
        <path d="M-20 300 C 100 280, 220 360, 340 320 S 460 260, 520 290" stroke="#372f2e" />
        <path d="M-20 440 C 140 420, 240 470, 380 440 S 480 410, 520 430" stroke="#372f2e" />
      </g>
      <g>
        <circle cx="120" cy="349" r="4" fill="#665a57" />
        <circle cx="300" cy="400" r="5" fill="#e3311d" />
        <circle cx="340" cy="320" r="4" fill="#665a57" />
        <circle cx="204" cy="443" r="3" fill="#4d4341" />
      </g>
    </svg>
  );
}

export function AuthLayout({ children }: { children: ReactNode }) {
  // Brand carriers: the sign-in logo and blurb are Settings-driven
  // ("Brand & theme"), so a rebrand shows up here too.
  const branding = useBranding();

  return (
    <div className="grid min-h-screen lg:grid-cols-[minmax(360px,44%)_1fr]">
      <aside className="relative hidden flex-col justify-between overflow-hidden bg-ink-950 p-10 lg:flex">
        <img
          src={branding.loginLogoUrl ?? import.meta.env.BASE_URL + "brand/BitweenFull-light.svg"}
          alt="Bitween"
          className="h-8 w-fit"
        />
        <div className="relative z-10 max-w-sm pb-24">
          <p className="text-2xl font-semibold leading-snug tracking-tight text-ink-50">
            The quiet middleman for everything your systems exchange.
          </p>
          <p className="mt-3 text-sm leading-relaxed text-ink-400">
            {branding.loginBlurb ??
              "Receive, transform and deliver documents between you and your partners — with every exchange traced."}
          </p>
        </div>
        <FlowLines />
      </aside>
      <main className="flex items-center justify-center px-4 py-10">
        <div className="w-full max-w-sm">
          <img
            src={branding.loginLogoUrl ?? import.meta.env.BASE_URL + "brand/BitweenFull.svg"}
            alt="Bitween"
            className="mb-8 h-7 w-fit lg:hidden"
          />
          {children}
        </div>
      </main>
    </div>
  );
}
