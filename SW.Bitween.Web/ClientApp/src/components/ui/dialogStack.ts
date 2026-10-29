
/**
 * Every dialog currently mounted, innermost last.
 *
 * Escape used to belong to all of them at once: a confirm opened from inside a
 * dialog, on a page with its own Escape handling, closed all three in one press
 * and threw away two levels of context nobody asked to leave. Only the top of the
 * stack responds now, and pages ask `dialogsOpen()` before claiming the key.
 */
export const openDialogs: symbol[] = [];

export const dialogsOpen = (): boolean => openDialogs.length > 0;
