import { createContext, useContext } from "react";
import type { SourceDocument } from "../config/sourceValues";

/** What the mapping can read from the original document. Null unless a response or bus gateway subscription's. */
export const SourceValuesContext = createContext<SourceDocument | null>(null);

export const useSourceValues = () => useContext(SourceValuesContext);
