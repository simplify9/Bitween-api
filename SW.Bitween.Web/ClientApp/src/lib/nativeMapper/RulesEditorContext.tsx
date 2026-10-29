import { useReducer, type ReactNode } from "react";
import { initialRulesEditorState, rulesEditorReducer } from "./rulesReducer";
import { StateContext, DispatchContext } from "./rulesEditorHooks";

export function RulesEditorProvider({ children }: { children: ReactNode }) {
  const [state, dispatch] = useReducer(rulesEditorReducer, initialRulesEditorState);
  return (
    <StateContext.Provider value={state}>
      <DispatchContext.Provider value={dispatch}>{children}</DispatchContext.Provider>
    </StateContext.Provider>
  );
}
