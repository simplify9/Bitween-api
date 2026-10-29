import { ApiRequestError } from "../../api/types";

/** Whether a load failed because the thing asked for isn't there (404). */
export const isNotFound = (error: unknown): boolean =>
  error instanceof ApiRequestError && (error.status === 404 || error.code === "NOT_FOUND");
