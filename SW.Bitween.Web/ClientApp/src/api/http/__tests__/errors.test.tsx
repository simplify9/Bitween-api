import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { server } from "../../../__tests__/support/server";
import { apiPath } from "../../../__tests__/support/renderApp";
import { ApiRequestError, shouldRetryQuery } from "../../types";
import { post } from "../request";

/** How a refusal from the API reaches the screen: what it says, and whether it is asked again. */
describe("errors from the API", () => {
  it("keeps every validation message, not only the first", async () => {
    server.use(
      http.post(apiPath("/things"), () =>
        HttpResponse.json({ Name: ["A name is needed."], Port: ["The port must be a number."] }, { status: 400 }),
      ),
    );

    const error = await post("/things", {}).catch((e: unknown) => e);

    expect(error).toBeInstanceOf(ApiRequestError);
    const refusal = error as ApiRequestError;
    expect(refusal.message).toBe("A name is needed. The port must be a number.");
    expect(refusal.code).toBe("Name");
    expect(refusal.status).toBe(400);
    expect(refusal.errors).toEqual({ Name: ["A name is needed."], Port: ["The port must be a number."] });
  });

  it("doesn't ask again for a refusal, but does for a server error or a dropped connection", () => {
    expect(shouldRetryQuery(0, new ApiRequestError("NOT_FOUND", "gone", 404))).toBe(false);
    expect(shouldRetryQuery(0, new ApiRequestError("HTTP_403", "no", 403))).toBe(false);
    expect(shouldRetryQuery(0, new ApiRequestError("ERROR", "boom", 500))).toBe(true);
    expect(shouldRetryQuery(0, new TypeError("Failed to fetch"))).toBe(true);
    expect(shouldRetryQuery(3, new ApiRequestError("ERROR", "boom", 500))).toBe(false);
  });
});
