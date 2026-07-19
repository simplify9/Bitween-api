import React, { createContext, useContext, useReducer } from 'react';
import { MappingEditorAction, MappingEditorState } from './mappingEditorActions';
import { initialMappingEditorState, mappingEditorReducer } from './mappingEditorReducer';

export * from './mappingEditorActions';
export * from './mappingEditorReducer';


// ─── Context ──────────────────────────────────────────────────────────────────

const MappingEditorStateContext = createContext<MappingEditorState | null>(null);
const MappingEditorDispatchContext = createContext<React.Dispatch<MappingEditorAction> | null>(null);

export const MappingEditorProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [state, dispatch] = useReducer(mappingEditorReducer, initialMappingEditorState);
  return (
    <MappingEditorStateContext.Provider value={state}>
      <MappingEditorDispatchContext.Provider value={dispatch}>
        {children}
      </MappingEditorDispatchContext.Provider>
    </MappingEditorStateContext.Provider>
  );
};

export function useMappingEditorState(): MappingEditorState {
  const ctx = useContext(MappingEditorStateContext);
  if (!ctx) throw new Error('useMappingEditorState must be used inside MappingEditorProvider');
  return ctx;
}

export function useMappingEditorDispatch(): React.Dispatch<MappingEditorAction> {
  const ctx = useContext(MappingEditorDispatchContext);
  if (!ctx) throw new Error('useMappingEditorDispatch must be used inside MappingEditorProvider');
  return ctx;
}
