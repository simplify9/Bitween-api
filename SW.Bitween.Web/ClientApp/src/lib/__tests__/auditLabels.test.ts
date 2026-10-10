import { describe, expect, it } from "vitest";
import { entityLabel, humanize, isBookkeeping, propertyLabel } from "../auditLabels";

describe("audit labels", () => {
  it("names kinds and fields as the screens do", () => {
    expect(entityLabel("Document")).toBe("Information type");
    expect(entityLabel("ApiCredential")).toBe("API key");
    expect(propertyLabel("Document", "BusEnabled")).toBe("Published to the bus");
    expect(propertyLabel("Partner", "SecretProperties")).toBe("Which settings are secret");
    expect(propertyLabel("Subscription", "HandlerId")).toBe("Delivery adapter");
  });

  it("makes words of any other name, and knows the database's own fields", () => {
    expect(humanize("UrlName")).toBe("Url name");
    expect(humanize("RunOnBadResponses")).toBe("Run on bad responses");
    expect(propertyLabel("Notifier", "RunOnFailure")).toBe("Run on failure");
    expect(isBookkeeping("CreatedOn")).toBe(true);
    expect(isBookkeeping("Name")).toBe(false);
  });
});
