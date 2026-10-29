import React, { useReducer } from 'react';
import { initialMappingEditorState, mappingEditorReducer } from './mappingEditorReducer';
import { MappingEditorStateContext, MappingEditorDispatchContext } from "./mappingEditorHooks";


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
