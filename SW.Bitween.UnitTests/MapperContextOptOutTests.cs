using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween;
using SW.Bitween.NativeAdapters;
using SW.PrimitiveTypes;

namespace SW.Bitween.UnitTests;

/// <summary>
/// <c>XchangeService.RunMapper</c> asks this before it parses the payload and writes
/// <c>__partner__</c> into it, so a wrong answer either strips a mapping's partner values or feeds
/// a non-JSON payload to <c>JToken.Parse</c>.
/// </summary>
[TestClass]
public class MapperContextOptOutTests
{
    /// <summary>A mapper that reads partner values out of the payload, like NativeJSONMapper.</summary>
    private sealed class NativePayloadMapper : INativeInfolinkMapper
    {
        public string Name => nameof(NativePayloadMapper);
        public Type StartupValuesType => typeof(object);
        public void InitializeStartupValues(IDictionary<string, string> settings) { }
        public Task<XchangeFile> Handle(XchangeFile xchangeFile) => Task.FromResult(xchangeFile);
    }

    /// <summary>A mapper that is handed its context instead.</summary>
    private sealed class NativeContextMapper : INativeInfolinkMapper, IReceivesMappingContext
    {
        public string Name => nameof(NativeContextMapper);
        public Type StartupValuesType => typeof(object);
        public void InitializeStartupValues(IDictionary<string, string> settings) { }
        public Task<XchangeFile> Handle(XchangeFile xchangeFile) => Task.FromResult(xchangeFile);
    }

    private static NativeAdapterDiscoveryService Discovery() =>
        new(
            nativeHandlers: [],
            nativeMappers: [new NativePayloadMapper(), new NativeContextMapper()],
            nativeReceivers: [],
            nativeValidators: [],
            nativeAdapters: [],
            bitweenOptions: new BitweenOptions());

    [TestMethod]
    public void MapperMarkedAsContextAware_OptsOut()
    {
        Assert.IsTrue(Discovery().MapperReceivesOwnContext(nameof(NativeContextMapper)));
    }

    [TestMethod]
    public void UnmarkedMapper_KeepsPayloadEnrichment()
    {
        Assert.IsFalse(Discovery().MapperReceivesOwnContext(nameof(NativePayloadMapper)));
    }

    /// <summary>
    /// Adapter ids are matched case-insensitively everywhere else in the discovery service, and this
    /// one is read from a database column, so it has to behave the same way.
    /// </summary>
    [TestMethod]
    public void MatchIsCaseInsensitive()
    {
        Assert.IsTrue(Discovery().MapperReceivesOwnContext("nativecontextmapper"));
        Assert.IsTrue(Discovery().MapperReceivesOwnContext("NATIVECONTEXTMAPPER"));
    }

    /// <summary>
    /// A serverless mapper is an id no native mapper answers to. It must keep the enrichment - the
    /// external adapter reads <c>__partner__</c> out of the payload like any other template.
    /// </summary>
    [TestMethod]
    public void ServerlessMapperId_KeepsPayloadEnrichment()
    {
        Assert.IsFalse(Discovery().MapperReceivesOwnContext("some.external.adapter"));
    }

    [TestMethod]
    public void NullOrEmptyId_KeepsPayloadEnrichment()
    {
        Assert.IsFalse(Discovery().MapperReceivesOwnContext(null));
        Assert.IsFalse(Discovery().MapperReceivesOwnContext(string.Empty));
    }

    /// <summary>
    /// The shipped mapper must never opt out: every template already in production reads
    /// <c>__partner__</c> and <c>__globals__</c> out of the payload.
    /// </summary>
    [TestMethod]
    public void NativeJSONMapper_IsNotContextAware()
    {
        Assert.IsFalse(new NativeJSONMapper() is IReceivesMappingContext);
    }
}
