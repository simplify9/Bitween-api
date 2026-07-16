import { useEffect } from "react";
import { useMappingEditorDispatch, redo, undo } from "../../lib/mapping/MappingEditorContext";

// Registers Ctrl/Cmd+Z (undo), Ctrl/Cmd+Y / Ctrl/Cmd+Shift+Z (redo),
// and Ctrl/Cmd+S (save) global keyboard shortcuts.
// Re-registers whenever onSave changes (i.e. when its deps like fieldMappings change).
export function useKeyboardShortcuts(onSave: () => void): void {
  const dispatch = useMappingEditorDispatch();

  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key === "z" && !e.shiftKey) {
        e.preventDefault();
        dispatch(undo());
      }
      if ((e.ctrlKey || e.metaKey) && (e.key === "y" || (e.key === "z" && e.shiftKey))) {
        e.preventDefault();
        dispatch(redo());
      }
      if ((e.ctrlKey || e.metaKey) && e.key === "s") {
        e.preventDefault();
        onSave();
      }
    };
    window.addEventListener("keydown", handler);
    return () => window.removeEventListener("keydown", handler);
  }, [onSave]);
}
