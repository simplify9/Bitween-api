/** The server's keys for each field it can refuse a save over: see Documents/Create.cs and Update.cs. */
export const INFORMATION_TYPE_ERRORS = {
  name: ["Name", "INVALID_NAME", "NAME_TAKEN"],
  code: ["Code", "CODE_TAKEN"],
  bus: ["BusMessageTypeName", "INVALID_BUS_TYPE_NAME"],
};
export type InformationTypeField = keyof typeof INFORMATION_TYPE_ERRORS;
