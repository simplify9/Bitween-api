import { useState, type ReactNode } from "react";
import { TriangleAlert } from "lucide-react";

/**
 * One thing a create form needs before it can be sent.
 *
 * The create pages used to answer an incomplete form three ways: the browser's own "Please fill
 * out this field" bubble, a Create button that stayed off with a sentence beside it, or a button
 * that let you press it and then said "Still needs a name" somewhere else on the page. Each form
 * now lists what it needs once, here, and they all behave alike: Create can always be pressed,
 * pressing it marks every field that is still wrong and puts the cursor in the first, and the
 * sentence beside the button keeps counting down what is left.
 */
export interface FormNeed<F extends string> {
  /** Which field it is about; the form shows `errors[field]` beside that field. */
  field: F;
  /** Whether it is still wrong. */
  failing: boolean;
  /** Its part of the "Still needs ..." sentence: "a name". */
  needs: string;
  /** Said at the field once Create has been pressed: "Give the group a name, e.g. Timeouts." */
  message: string;
  /**
   * The id of what to put the cursor in. A field that isn't on screen (a closed pipeline step)
   * points at its FieldProblem instead and opens the way to it with `reveal`.
   */
  focus?: string;
  /** Brings the field on screen first, e.g. selects the pipeline step it lives in. */
  reveal?: () => void;
}

export function useFormCheck<F extends string>(needs: (FormNeed<F> | false | null | undefined)[]) {
  // Nothing is marked until Create has been pressed: a form that opens covered in red is
  // telling you off for not having typed yet.
  const [tried, setTried] = useState(false);
  const failing = needs.filter((n): n is FormNeed<F> => !!n && n.failing);

  const errors: Partial<Record<F, string>> = {};
  if (tried) for (const n of failing) errors[n.field] ??= n.message;

  return {
    errors,
    /** What is left, for the StillNeeds sentence beside the button. */
    missing: [...new Set(failing.map((n) => n.needs))],
    /** Runs `send` when nothing is missing; otherwise marks the fields and focuses the first. */
    submit: (send: () => void) => {
      if (failing.length === 0) return send();
      setTried(true);
      const first = failing[0];
      if (!first.focus) return;
      const id = first.focus;
      const go = () => {
        const el = document.getElementById(id);
        el?.focus();
        el?.scrollIntoView?.({ block: "center" });
      };
      // Revealing re-renders, and the field (or its problem line) only exists after that.
      if (first.reveal) {
        first.reveal();
        setTimeout(go, 0);
      } else go();
    },
  };
}

/**
 * The live checklist beside a create button: "Still needs a name and a delivery." Plain ink
 * rather than red - it is a count of what is left, not a complaint.
 */
export function StillNeeds({ missing, className = "" }: { missing: string[]; className?: string }) {
  if (missing.length === 0) return null;
  return (
    <p aria-live="polite" className={`text-[13px] text-ink-500 ${className}`}>
      Still needs {missing.slice(0, -1).join(", ")}
      {missing.length > 1 ? " and " : ""}
      {missing.at(-1)}.
    </p>
  );
}

/**
 * A field's problem, for a control that isn't inside a Field (a pipeline step, a picker in a
 * section of its own). Same look as Field's own error line. Focusable, so a form whose first
 * problem is one of these can still move the cursor to it.
 */
export function FieldProblem({ id, children }: { id?: string; children: ReactNode }) {
  if (!children) return null;
  return (
    <p id={id} tabIndex={-1} role="alert" className="flex items-start gap-1 text-[13px] text-danger-700 focus:outline-none">
      <TriangleAlert className="mt-0.5 size-3.5 shrink-0" aria-hidden />
      <span>{children}</span>
    </p>
  );
}
