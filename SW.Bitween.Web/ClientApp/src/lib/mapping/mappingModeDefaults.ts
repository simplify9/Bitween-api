import { FieldMapping, MappingMode } from './types';

// Default field values to apply when switching a leaf to a given mode.
// For 'fixed', fixedValue is overridden at runtime with a sample from outputJson.
export const MODE_INITIAL_FIELDS: Record<MappingMode, Partial<FieldMapping>> = {
  fixed:   { source: '', fixedValue: '', partnerPropKey: undefined, globalSetId: undefined, globalKey: undefined, valuesSetId: undefined, lookupDictionary: undefined, transform: undefined },
  partner: { source: '', fixedValue: undefined, partnerPropKey: '', globalSetId: undefined, globalKey: undefined, valuesSetId: undefined, lookupDictionary: undefined, transform: undefined },
  global:  { source: '', fixedValue: undefined, partnerPropKey: undefined, globalSetId: '', globalKey: '', valuesSetId: undefined, lookupDictionary: undefined, transform: undefined },
  source:  { fixedValue: undefined, partnerPropKey: undefined, globalSetId: undefined, globalKey: undefined, valuesSetId: undefined },
};
