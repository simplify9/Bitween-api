import { describe, expect, it } from "vitest";
import { ApiRequestError } from "../../api/types";
import { splitErrors } from "../fieldErrors";

const refused = (errors: Record<string, string[]>) =>
  new ApiRequestError(Object.keys(errors)[0], Object.values(errors).flat().join(" "), 400, errors);

const fields = { name: ["Name", "NAME_TAKEN"], code: ["CODE_TAKEN"] };

describe("splitErrors", () => {
  it("puts each message under the field its key is about, whatever the case", () => {
    const { byField, rest } = splitErrors(
      refused({ name: ["'Name' must not be empty."], CODE_TAKEN: ["This code is already in use."] }),
      fields,
    );
    expect(byField).toEqual({ name: "'Name' must not be empty.", code: "This code is already in use." });
    expect(rest).toBe("Check the fields marked in red.");
  });

  it("keeps what no field claims for the form, once", () => {
    const { byField, rest } = splitErrors(
      refused({ NAME_TAKEN: ["Taken."], SWException: ["Not allowed.", "Not allowed."] }),
      fields,
    );
    expect(byField).toEqual({ name: "Taken." });
    expect(rest).toBe("Not allowed.");
  });

  it("passes an error that isn't a validation refusal through whole", () => {
    expect(splitErrors(new Error("Network down."), fields)).toEqual({ byField: {}, rest: "Network down." });
    expect(splitErrors(null, fields)).toEqual({ byField: {}, rest: "" });
  });
});
