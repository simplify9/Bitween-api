import { useState } from "react";
import { Check, Copy } from "lucide-react";

/** A read-only mono value with a copy button — invite links, reset links. */
export function CopyField({ value, label }: { value: string; label?: string }) {
  const [copied, setCopied] = useState(false);

  const copy = async () => {
    await navigator.clipboard.writeText(value);
    setCopied(true);
    setTimeout(() => setCopied(false), 1600);
  };

  return (
    <div>
      {label && <p className="mb-1 text-[13px] font-medium text-ink-700">{label}</p>}
      <div className="flex items-center gap-1.5 rounded-lg border border-ink-200 bg-ink-50 py-1.5 pr-1.5 pl-3">
        <code className="min-w-0 flex-1 truncate font-mono text-xs text-ink-700">{value}</code>
        <button
          onClick={copy}
          className="flex h-7 shrink-0 items-center gap-1 rounded-md bg-white px-2 text-xs font-medium text-ink-700 shadow-sm ring-1 ring-ink-200 hover:bg-ink-50"
        >
          {copied ? <Check className="size-3.5 text-ok-600" /> : <Copy className="size-3.5" />}
          {copied ? "Copied" : "Copy"}
        </button>
      </div>
    </div>
  );
}
