import { useState } from "react";
import { Check, Copy } from "lucide-react";

/** Copies a value and shows a tick for a moment. */
export function CopyButton({
  value,
  label,
  className = "rounded-md p-1 text-ink-500 hover:bg-ink-100 hover:text-ink-700",
}: {
  value: string;
  label: string;
  /** Overridden by the document toolbar, which sits on a dark ground. */
  className?: string;
}) {
  const [copied, setCopied] = useState(false);
  return (
    <button
      onClick={async () => {
        await navigator.clipboard.writeText(value);
        setCopied(true);
        setTimeout(() => setCopied(false), 1400);
      }}
      title={`Copy ${label}`}
      className={className}
    >
      {copied ? <Check className="size-3.5 text-ok-600" /> : <Copy className="size-3.5" />}
    </button>
  );
}
