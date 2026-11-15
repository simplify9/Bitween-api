import { cleanup, screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse, type RequestHandler } from "msw";
import { afterEach, describe, expect, it } from "vitest";
import type { SettingRow } from "../../../api";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";
import { settingsDraft } from "../../../lib/settingsDraft";

const row = (key: string, label: string, value: string, extra: Partial<SettingRow> = {}): SettingRow => ({
  key,
  section: "AI",
  label,
  kind: "string",
  description: "",
  defaultValue: "",
  value,
  secret: false,
  overridden: value !== "",
  hasValue: value !== "",
  editable: true,
  access: "editable",
  ...extra,
});

/** As GET /aichecks/providers describes them: one of each way an address is made. */
const PROVIDERS = [
  { name: "Anthropic", address: "https://api.anthropic.com", keyless: false, lists: true },
  {
    name: "Azure OpenAI",
    template: "https://{0}.openai.azure.com/openai/v1/",
    part: "Resource name",
    example: "acme-ai",
    keyless: false,
    lists: false,
    modelHint: "Type the deployment name.",
  },
  {
    name: "Amazon Bedrock",
    template: "https://bedrock-runtime.{0}.amazonaws.com/openai/v1/",
    part: "AWS region",
    example: "us-east-1",
    keyless: false,
    lists: false,
    modelHint: "Type the model ID from the Bedrock console.",
  },
  { name: "Google Gemini", address: "https://generativelanguage.googleapis.com/v1beta/openai/", keyless: false, lists: true },
  {
    name: "Alibaba Cloud (Qwen)",
    address: "https://dashscope-intl.aliyuncs.com/compatible-mode/v1/",
    places: [
      { label: "Singapore", address: "https://dashscope-intl.aliyuncs.com/compatible-mode/v1/" },
      { label: "US (Virginia)", address: "https://dashscope-us.aliyuncs.com/compatible-mode/v1/" },
    ],
    keyless: false,
    lists: true,
  },
  { name: "Ollama", address: "http://localhost:11434/v1/", keyless: true, lists: true },
];

/** Settings, AI with Gemini saved, its key stored as a secret. */
const ROWS: SettingRow[] = [
  row("Bitween.Ai.Provider", "Provider", "Google Gemini", {
    choices: PROVIDERS.map((p) => p.name),
    defaultValue: "Anthropic",
  }),
  row("Bitween.Ai.ApiKey", "API key", "", { secret: true, value: null, hasValue: true, overridden: true }),
  row("Bitween.Ai.Model", "Model", "gemini-flash"),
  row("Bitween.Ai.BaseUrl", "API address", ""),
];

const openAi = (handlers: RequestHandler[] = []) =>
  renderApp("/settings?section=AI", {
    // A test's own handlers first: the first that matches a request answers it.
    handlers: [
      ...handlers,
      http.get(apiPath("/settings"), () => HttpResponse.json(ROWS)),
      http.get(apiPath("/audit"), () => HttpResponse.json({ result: [], totalCount: 0 })),
      http.get(apiPath("/aichecks/providers"), () => HttpResponse.json(PROVIDERS)),
      http.post(apiPath("/aichecks/models"), () => HttpResponse.json({ models: ["gemini-flash", "gemini-pro"] })),
    ],
  });

afterEach(() => {
  cleanup();
  settingsDraft.discardAll();
});

describe("Settings, AI", () => {
  it("offers the provider's models to pick from, and still takes a name typed in", async () => {
    let asked: unknown;
    const { user } = openAi([
      http.post(apiPath("/aichecks/models"), async ({ request }) => {
        asked = await request.json();
        return HttpResponse.json({ models: ["gemini-flash", "gemini-pro"] });
      }),
    ]);

    expect(await screen.findByText("2 models from Google Gemini.")).toBeVisible();
    // No key was typed, so none is sent: the server uses the saved one.
    expect(asked).toEqual({ provider: "Google Gemini", model: "", baseUrl: "" });

    await user.click(screen.getByRole("combobox", { name: "Model" }));
    await user.click(await screen.findByRole("option", { name: "gemini-pro" }));
    expect(screen.getByText("Unsaved", { exact: true })).toBeVisible();

    await user.clear(screen.getByRole("combobox", { name: "Model" }));
    await user.type(screen.getByRole("combobox", { name: "Model" }), "gemini-next");
    expect(await screen.findByRole("option", { name: "Use “gemini-next”" })).toBeVisible();
  });

  it("asks for the deployment's name on Azure, which lists no models", async () => {
    const { user } = openAi([
      http.post(apiPath("/aichecks/models"), () => HttpResponse.json({ models: null })),
    ]);

    await user.selectOptions(await screen.findByRole("combobox", { name: "Provider" }), "Azure OpenAI");

    expect(await screen.findByText("Type the deployment name.")).toBeVisible();
  });

  it("tests what's on screen and says how it went", async () => {
    let tested: unknown;
    const { user } = openAi([
      http.post(apiPath("/aichecks"), async ({ request }) => {
        tested = await request.json();
        return HttpResponse.json({ model: "gemini-flash", milliseconds: 1234 });
      }),
    ]);

    await user.click(await screen.findByRole("button", { name: "Test connection" }));

    expect(await screen.findByText("Works")).toBeVisible();
    expect(screen.getByText(/answered in 1\.2 s/)).toBeVisible();
    expect(tested).toEqual({ provider: "Google Gemini", model: "gemini-flash", baseUrl: "" });
  });

  it("tests changed AI settings before saving, and saves only when asked to after a failure", async () => {
    const saved: string[] = [];
    const { user } = openAi([
      http.post(apiPath("/aichecks"), () =>
        HttpResponse.json({ AI_PROVIDER_ERROR: ["Gemini refused the request: there's no model called 'nope'."] }, { status: 400 }),
      ),
      http.post(apiPath("/settings/:key"), ({ params }) => {
        saved.push(String(params.key));
        return new HttpResponse(null, { status: 204 });
      }),
    ]);

    await user.clear(await screen.findByRole("combobox", { name: "Model" }));
    await user.type(screen.getByRole("combobox", { name: "Model" }), "nope");
    await user.click(await screen.findByRole("option", { name: "Use “nope”" }));
    await user.click(screen.getByRole("button", { name: "Save changes" }));

    const dialog = within(await screen.findByRole("dialog"));
    expect(dialog.getByText(/no model called 'nope'/)).toBeVisible();
    expect(saved).toEqual([]);

    await user.click(dialog.getByRole("button", { name: "Save anyway" }));
    await waitFor(() => expect(saved).toEqual(["Bitween.Ai.Model"]));
  });

  it("saves without a test call when the key is being cleared, turning AI off", async () => {
    let tested = false;
    const saved: string[] = [];
    const { user } = openAi([
      http.post(apiPath("/aichecks"), () => {
        tested = true;
        return HttpResponse.json({ model: "x", milliseconds: 1 });
      }),
      http.delete(apiPath("/settings/:key"), ({ params }) => {
        saved.push(String(params.key));
        return new HttpResponse(null, { status: 204 });
      }),
    ]);

    const keyRow = (await screen.findByText("API key")).closest("div.grid") as HTMLElement;
    await user.click(within(keyRow).getByRole("button", { name: "Reset to default" }));
    await user.click(screen.getByRole("button", { name: "Save changes" }));

    await waitFor(() => expect(saved).toEqual(["Bitween.Ai.ApiKey"]));
    expect(tested).toBe(false);
  });
  it("shows a provider's own address, and takes another only when asked for one", async () => {
    const { user } = openAi();

    expect(await screen.findByText("https://generativelanguage.googleapis.com/v1beta/openai/")).toBeVisible();
    expect(screen.queryByRole("textbox", { name: "API address" })).toBeNull();

    await user.click(screen.getByRole("button", { name: "Use a different address" }));
    await user.type(screen.getByRole("textbox", { name: "API address" }), "https://proxy.acme.com/v1/");
    await user.tab();

    expect(settingsDraft.get()["Bitween.Ai.BaseUrl"]).toBe("https://proxy.acme.com/v1/");
  });

  it("asks Bedrock for the region, builds the address from it, and empties the last provider's key and model", async () => {
    const saved: Record<string, unknown> = {};
    const { user } = openAi([
      http.post(apiPath("/aichecks"), () => HttpResponse.json({ model: "m", milliseconds: 1 })),
      http.post(apiPath("/settings/:key"), async ({ params, request }) => {
        saved[String(params.key)] = ((await request.json()) as { value: string }).value;
        return new HttpResponse(null, { status: 204 });
      }),
    ]);

    await user.selectOptions(await screen.findByRole("combobox", { name: "Provider" }), "Amazon Bedrock");
    expect(screen.getByText("Type the model ID from the Bedrock console.")).toBeVisible();
    // Gemini's key is no use to Bedrock: the box is empty, waiting for Bedrock's.
    expect(screen.getByRole("textbox", { name: "API key" })).toHaveValue("");

    await user.type(screen.getByRole("textbox", { name: "AWS region" }), "eu-central-1");
    await user.tab();
    expect(screen.getByText("https://bedrock-runtime.eu-central-1.amazonaws.com/openai/v1/")).toBeVisible();

    await user.click(screen.getByRole("button", { name: "Save changes" }));
    await waitFor(() =>
      expect(saved["Bitween.Ai.BaseUrl"]).toBe("https://bedrock-runtime.eu-central-1.amazonaws.com/openai/v1/"),
    );
    expect(saved["Bitween.Ai.Provider"]).toBe("Amazon Bedrock");
  });

  it("offers Qwen's regions, and going back to the saved provider brings its settings back", async () => {
    const { user } = openAi();

    await user.selectOptions(await screen.findByRole("combobox", { name: "Provider" }), "Alibaba Cloud (Qwen)");
    await user.selectOptions(screen.getByRole("combobox", { name: "Region" }), "US (Virginia)");
    expect(settingsDraft.get()["Bitween.Ai.BaseUrl"]).toBe("https://dashscope-us.aliyuncs.com/compatible-mode/v1/");

    await user.selectOptions(screen.getByRole("combobox", { name: "Provider" }), "Google Gemini");
    expect(settingsDraft.get()).toEqual({});
  });
});
