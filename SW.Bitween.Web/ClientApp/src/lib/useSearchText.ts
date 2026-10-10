import { useEffect, useRef, useState } from "react";

/**
 * A search box's text, kept apart from the search it runs.
 *
 * Every keystroke used to rewrite the URL, and on a list searched on the server every rewrite
 * was a request: "orders" asked six times. The box now answers at once and the search follows
 * once typing pauses. When the search changes some other way — Back, a link, a cleared filter —
 * the box shows what it now is.
 */
export function useSearchText(search: string, run: (text: string) => void, delayMs = 250) {
  const [text, setText] = useState(search);
  const sent = useRef(search);
  const runRef = useRef(run);
  runRef.current = run;

  useEffect(() => {
    if (search === sent.current) return;
    sent.current = search;
    setText(search);
  }, [search]);

  useEffect(() => {
    if (text === sent.current) return;
    const timer = setTimeout(() => {
      sent.current = text;
      runRef.current(text);
    }, delayMs);
    return () => clearTimeout(timer);
  }, [text, delayMs]);

  return [text, setText] as const;
}
