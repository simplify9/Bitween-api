import React, { createContext, useContext } from 'react';
import { MappingEditorAction, MappingEditorState } from './mappingEditorActions';

// ─── Context ──────────────────────────────────────────────────────────────────

export const MappingEditorStateContext = createContext<MappingEditorState | null>(null);
export const MappingEditorDispatchContext = createContext<React.Dispatch<MappingEditorAction> | null>(null);

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
