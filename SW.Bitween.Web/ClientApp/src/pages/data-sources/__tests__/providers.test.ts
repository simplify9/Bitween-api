import { describe, expect, it } from "vitest";

import type { DataSourceProvider } from "../../../api/types";
import {
  declaredSecrets,
  initialProperties,
  isSecretName,
  orderedSettingNames,
  settingOf,
} from "../providers";

const rabbit: DataSourceProvider = {
  adapterId: "bitween.bus.rabbitmq",
  label: "RabbitMQ",
  kind: "Broker",
  description: "An AMQP broker the customer runs.",
  settings: [
    { name: "Host", type: "string", hint: null, default: null, allowedValues: null, secret: false, required: true },
    { name: "Port", type: "number", hint: null, default: "5672", allowedValues: null, secret: false, required: false },
    { name: "Password", type: "string", hint: null, default: null, allowedValues: null, secret: true, required: true },
    { name: "QueueType", type: "string", hint: null, default: null, allowedValues: ["classic", "quorum", "stream"], secret: false, required: false },
    { name: "Exchange", type: "string", hint: null, default: null, allowedValues: null, secret: false, required: false },
  ],
};

describe("initialProperties", () => {
  it("starts a data source with what the adapter requires and whatever it gave a default", () => {
    expect(initialProperties(rabbit)).toEqual({ Host: "", Port: "5672", Password: "" });
  });

  it("leaves the optional rest off the form", () => {
    // Twenty mostly-empty boxes hide the four that matter; Exchange is added by hand when wanted.
    expect(initialProperties(rabbit)).not.toHaveProperty("Exchange");
    expect(initialProperties(rabbit)).not.toHaveProperty("QueueType");
  });
});

describe("orderedSettingNames", () => {
  it("renders in the adapter's order, not the order the map came back in", () => {
    // The server hands back a dictionary, and its order is whatever the database and serializer
    // produced - which is how the form once asked for a password before the username it belongs to.
    expect(orderedSettingNames(rabbit, ["Password", "Port", "Host"])).toEqual([
      "Host",
      "Port",
      "Password",
    ]);
  });

  it("matches whatever case the property was saved in", () => {
    expect(orderedSettingNames(rabbit, ["password", "host"])).toEqual(["host", "password"]);
  });

  it("puts hand-added properties after the declared ones, in their own order", () => {
    expect(orderedSettingNames(rabbit, ["Tls", "Password", "Host"])).toEqual([
      "Host",
      "Password",
      "Tls",
    ]);
  });

  it("changes nothing when the catalog has not arrived", () => {
    // The form still renders while the provider list loads; reordering to nothing would make the
    // fields jump once it lands.
    expect(orderedSettingNames(undefined, ["Password", "Host"])).toEqual(["Password", "Host"]);
  });
});

describe("settingOf", () => {
  it("finds a setting whatever case the stored property key is in", () => {
    // Properties come back from the server in whatever case they were saved, and the adapter
    // binds them case-insensitively - the form has to match the same way or a saved "host" would
    // render as an unknown extra with no hint and no type.
    expect(settingOf(rabbit, "host")?.name).toBe("Host");
    expect(settingOf(rabbit, "QUEUETYPE")?.allowedValues).toEqual(["classic", "quorum", "stream"]);
  });

  it("returns nothing for a property the adapter never declared", () => {
    expect(settingOf(rabbit, "Tls")).toBeUndefined();
  });
});

describe("isSecretName", () => {
  it("masks what the adapter declared secret, whatever it is called", () => {
    expect(isSecretName("Password", [], settingOf(rabbit, "Password"))).toBe(true);
  });

  it("still masks a hand-added property that looks like a credential", () => {
    // Nothing declares this one, so the heuristic is all there is - it is the same rule the
    // server masks by.
    expect(isSecretName("ClientSecret")).toBe(true);
    expect(isSecretName("SharedAccessKey")).toBe(true);
  });

  it("leaves an ordinary setting alone", () => {
    expect(isSecretName("Host", [], settingOf(rabbit, "Host"))).toBe(false);
  });
});

describe("declaredSecrets", () => {
  it("names the credentials so the server masks them from the first save", () => {
    expect(declaredSecrets(rabbit)).toEqual(["Password"]);
  });
});
