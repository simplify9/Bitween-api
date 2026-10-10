import { createContext, useContext, type Dispatch } from "react";
import { type RulesEditorAction, type RulesEditorState } from "./rulesReducer";

export const StateContext = createContext<RulesEditorState | null>(null);
export const DispatchContext = createContext<Dispatch<RulesEditorAction> | null>(null);

export function useRules(): RulesEditorState {
  const state = useContext(StateContext);
  if (!state) throw new Error("useRules must be used inside RulesEditorProvider");
  return state;
}

export function useRulesDispatch(): Dispatch<RulesEditorAction> {
  const dispatch = useContext(DispatchContext);
  if (!dispatch) throw new Error("useRulesDispatch must be used inside RulesEditorProvider");
  return dispatch;
}
