import {
  createContext,
  useContext,
  useId,
  useState,
  type InputHTMLAttributes,
  type ReactNode,
  type SelectHTMLAttributes,
} from "react";
import { Eye, EyeOff } from "lucide-react";

const inputClass =
  "h-9.5 w-full rounded-lg border border-ink-200 bg-white px-3 text-sm text-ink-900 placeholder:text-ink-400 focus:border-focus-400 focus:outline-none focus:ring-2 focus:ring-focus-100 aria-invalid:border-danger-600 disabled:bg-ink-50 disabled:text-ink-500";

/**
 * What a Field tells the control inside it: the id its label points at, the note under it that
 * describes it, and whether that note is an error. A control's own props win.
 */
const FieldContext = createContext<{ id?: string; describedBy?: string; invalid: boolean } | null>(null);

function useFieldProps<P extends { id?: string; "aria-describedby"?: string; "aria-invalid"?: unknown }>(props: P): P {
  const field = useContext(FieldContext);
  if (!field) return props;
  return {
    ...props,
    id: props.id ?? field.id,
    "aria-describedby": props["aria-describedby"] ?? field.describedBy,
    "aria-invalid": props["aria-invalid"] ?? (field.invalid || undefined),
  };
}

/**
 * A label, the control it names, and a hint or an error under it.
 *
 * Without `htmlFor` the Field gives its control an id of its own, so the label names it for a
 * screen reader and a click on the label focuses it. A Field holding several controls passes
 * `htmlFor` and gives the ids itself.
 */
export function Field({
  label,
  hint,
  error,
  children,
  htmlFor,
}: {
  label: string;
  hint?: ReactNode;
  error?: string;
  children: ReactNode;
  htmlFor?: string;
}) {
  const own = useId();
  const id = htmlFor ?? own;
  const noteId = `${id}-note`;
  const note = error || hint;
  return (
    <FieldContext.Provider value={{ id: htmlFor ? undefined : id, describedBy: note ? noteId : undefined, invalid: !!error }}>
      <div className="space-y-1.5">
        <label htmlFor={id} className="block text-[13px] font-medium text-ink-700">
          {label}
        </label>
        {children}
        {error ? (
          <p id={noteId} role="alert" className="text-[13px] text-danger-700">
            {error}
          </p>
        ) : (
          hint && (
            <p id={noteId} className="text-[13px] text-ink-500">
              {hint}
            </p>
          )
        )}
      </div>
    </FieldContext.Provider>
  );
}

export function TextInput(own: InputHTMLAttributes<HTMLInputElement>) {
  const props = useFieldProps(own);
  return <input {...props} className={`${inputClass} ${props.className ?? ""}`} />;
}

export function PasswordInput(own: InputHTMLAttributes<HTMLInputElement>) {
  const props = useFieldProps(own);
  const [visible, setVisible] = useState(false);
  return (
    <div className="relative">
      <input
        {...props}
        type={visible ? "text" : "password"}
        className={`${inputClass} pr-10 ${props.className ?? ""}`}
      />
      <button
        type="button"
        onClick={() => setVisible((v) => !v)}
        aria-label={visible ? "Hide password" : "Show password"}
        className="absolute inset-y-0 right-0 flex w-10 items-center justify-center text-ink-500 hover:text-ink-600"
      >
        {visible ? <EyeOff className="size-4" /> : <Eye className="size-4" />}
      </button>
    </div>
  );
}

export function Checkbox({
  label,
  description,
  ...props
}: InputHTMLAttributes<HTMLInputElement> & { label: ReactNode; description?: ReactNode }) {
  const id = useId();
  return (
    <label htmlFor={id} className="flex cursor-pointer items-start gap-2.5">
      <input
        id={id}
        type="checkbox"
        {...props}
        className="mt-0.5 size-4 shrink-0 cursor-pointer rounded accent-crimson-600"
      />
      <span className="min-w-0">
        <span className="block text-sm font-medium text-ink-800">{label}</span>
        {description && <span className="block text-[13px] text-ink-500">{description}</span>}
      </span>
    </label>
  );
}

export function Select({
  options,
  ...own
}: SelectHTMLAttributes<HTMLSelectElement> & {
  options: { value: string; label: string }[];
}) {
  const props = useFieldProps(own);
  return (
    <select
      {...props}
      className={`h-9.5 w-full cursor-pointer rounded-lg border border-ink-200 bg-white px-2.5 text-sm text-ink-900 focus:border-focus-400 focus:ring-2 focus:ring-focus-100 focus:outline-none aria-invalid:border-danger-600 disabled:bg-ink-50 disabled:text-ink-500 ${props.className ?? ""}`}
    >
      {options.map((o) => (
        <option key={o.value} value={o.value}>
          {o.label}
        </option>
      ))}
    </select>
  );
}
