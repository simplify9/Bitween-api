import { describe, expect, it } from "vitest";
import { summarizeException } from "../exceptionSummary";

describe("summarizeException", () => {
  it("takes the message from a .NET exception and leaves the stack for the details", () => {
    const s = summarizeException(
      "System.Net.Http.HttpRequestException: Connection refused (127.0.0.1:9)\n" +
        " ---> System.Net.Sockets.SocketException (61): Connection refused\n" +
        "   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.ThrowException(SocketError error)\n" +
        "   at SW.Bitween.NativeAdapters.OutboundAddressGuard in /Users/someone/src/Guard.cs:line 43",
    );
    expect(s.summary).toBe("Connection refused (127.0.0.1:9)");
    expect(s.kind).toBe("HttpRequestException");
    expect(s.hasDetail).toBe(true);
  });

  it("looks past a wrapper whose own message says nothing", () => {
    const s = summarizeException(
      "System.AggregateException: One or more errors occurred. (The partner said 503)\n" +
        " ---> SW.Bitween.PartnerRejectedException: The partner said 503\n   at X.Y()",
    );
    expect(s.summary).toBe("The partner said 503");
    expect(s.kind).toBe("PartnerRejectedException");
  });

  it("keeps a plain message as it is", () => {
    expect(summarizeException("Not found")).toEqual({ summary: "Not found", kind: null, hasDetail: false });
    expect(summarizeException(null).summary).toBe("");
  });
});
