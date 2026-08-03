import { useState } from "react";

/**
 * Draft state that survives the routed create/edit detours of the return-flow:
 * persisted in sessionStorage, cleared on submit.
 *
 * All that is left of the old `config/wizard.tsx`. The step navigation it lived
 * beside is gone — create flows are one form now — but the detours it exists for
 * ("create the partner on its own page, then continue right here") are not, so
 * this outlives the wizards.
 */
export function usePersistentDraft<T>(key: string, initial: T) {
  const [draft, setDraft] = useState<T>(() => {
    try {
      const raw = sessionStorage.getItem(key);
      if (raw) return JSON.parse(raw) as T;
    } catch {
      // corrupted draft — start fresh
    }
    return initial;
  });

  const update = (patch: Partial<T>) =>
    setDraft((d) => {
      const next = { ...d, ...patch };
      sessionStorage.setItem(key, JSON.stringify(next));
      return next;
    });

  const clear = () => sessionStorage.removeItem(key);

  return [draft, update, clear] as const;
}
