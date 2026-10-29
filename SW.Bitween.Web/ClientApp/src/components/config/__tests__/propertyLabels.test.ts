import { describe, expect, it } from "vitest";
import { groupSettings, guessGroup, propertyLabel, sectionSummary } from "../propertyLabels";

describe("propertyLabel", () => {
  it.each([
    ["ApiKey", "API key"],
    ["AuthType", "Auth type"],
    ["LoginUrl", "Login URL"],
    ["LoginUsername", "Login user name"],
    ["UserName", "User name"],
    ["Username", "User name"],
    ["ContentType", "Content type"],
    ["CorrelationId", "Correlation ID"],
    ["TimeoutSeconds", "Timeout (seconds)"],
    ["RetryDelayMs", "Retry delay (ms)"],
    ["InstanceUrl", "Instance URL"],
    ["UseSsl", "Use SSL"],
    ["SftpHost", "SFTP host"],
    ["OauthScope", "OAuth scope"],
    ["Oauth2Token", "OAuth2 token"],
    ["JSONPath", "JSON path"],
    ["client_id", "Client ID"],
    ["xml_root", "XML root"],
    ["Url", "URL"],
    ["Seconds", "Seconds"],
  ])("%s reads %s", (key, label) => {
    expect(propertyLabel(key)).toBe(label);
  });

  it("prefers the adapter's own display name", () => {
    expect(propertyLabel("Verb", "HTTP method")).toBe("HTTP method");
    expect(propertyLabel("Verb", "  ")).toBe("Verb");
  });
});

describe("guessGroup", () => {
  it.each([
    ["AuthType", "Authentication"],
    ["ApiKey", "Authentication"],
    ["LoginUrl", "Authentication"],
    ["LoginBody", "Authentication"],
    ["ClientSecret", "Authentication"],
    ["SmtpPassword", "Authentication"],
    ["Verb", "Request"],
    ["Headers", "Request"],
    ["ContentType", "Request"],
    ["DefaultRequest", "Request"],
    ["Url", "Request"],
    ["Host", "Connection"],
    ["Port", "Connection"],
    ["TimeoutSeconds", "Advanced"],
    ["CorrelationId", "Advanced"],
    ["BatchSize", "Advanced"],
  ])("%s goes in %s", (key, group) => {
    expect(guessGroup(key)).toBe(group);
  });
});

describe("groupSettings", () => {
  it("guesses fixed sections in a fixed order, and makes only the ones with something in them", () => {
    const groups = groupSettings(["TimeoutSeconds", "ApiKey", "Verb", "Headers"].map((key) => ({ key })));
    expect(groups.map((g) => [g.name, g.items.map((i) => i.key)])).toEqual([
      ["Request", ["Verb", "Headers"]],
      ["Authentication", ["ApiKey"]],
      ["Advanced", ["TimeoutSeconds"]],
    ]);
  });

  it("uses a declared group as it is, first, and folds a guessed section into a declared one of the same name", () => {
    const groups = groupSettings([
      { key: "Region", group: "Where" },
      { key: "Retries", group: "advanced" },
      { key: "TimeoutSeconds" },
      { key: "Bucket", group: "Where" },
    ]);
    expect(groups.map((g) => [g.name, g.items.map((i) => i.key)])).toEqual([
      ["Where", ["Region", "Bucket"]],
      ["advanced", ["Retries", "TimeoutSeconds"]],
    ]);
  });

  it("is empty with nothing optional", () => {
    expect(groupSettings([])).toEqual([]);
  });
});

describe("sectionSummary", () => {
  const item = (label: string, value: string, secret = false) => ({ label, value, secret });

  it("says nothing is set", () => {
    expect(sectionSummary([item("Verb", ""), item("Headers", "")])).toBe("none set");
  });

  it("shows short values, names secrets and long values without showing them", () => {
    expect(
      sectionSummary([
        item("Verb", "post"),
        item("Content type", "application/json"),
        item("Headers", "X-Key=abc", true),
        item("Default request", ""),
      ]),
    ).toBe("post, application/json, Headers set");
    expect(sectionSummary([item("Login body", '{"user":"someone","password":"x"}')])).toBe("Login body set");
  });

  it("counts instead of listing more than three", () => {
    expect(sectionSummary(["a", "b", "c", "d", ""].map((v, i) => item(`S${i}`, v)))).toBe("4 of 5 set");
  });
});
