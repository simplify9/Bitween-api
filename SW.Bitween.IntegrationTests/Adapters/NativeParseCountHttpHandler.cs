using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SW.Bitween.NativeAdapters;
using SW.PrimitiveTypes;

namespace SW.Bitween.IntegrationTests.Adapters;

/// <summary>
/// The built-in HTTP handler, unchanged, on a client that answers 200 in memory: its URL template is
/// filled from the payload exactly as in production, and nothing leaves the process.
/// </summary>
/// <remarks>
/// For <c>ExchangeParseCountTests</c>, which counts the handler's parse of the payload for a templated
/// URL. A wrapper rather than a registration of <see cref="NativeHttpHandler"/> itself, which other
/// tests use as an id that is never run.
/// </remarks>
public class NativeParseCountHttpHandler : INativeInfolinkHandler
{
    private readonly NativeHttpHandler inner = new(new Proxy());

    public string Name => nameof(NativeParseCountHttpHandler);
    public Type StartupValuesType => inner.StartupValuesType;

    public void InitializeStartupValues(IDictionary<string, string> settings) => inner.InitializeStartupValues(settings);

    public Task<XchangeFile> Handle(XchangeFile xchangeFile) => inner.Handle(xchangeFile);

    private sealed class Proxy : IDynamicHttpProxy
    {
        private static readonly HttpClient Client = new(new Answer());

        public HttpClient GetClient(string fullUrl) => Client;
    }

    private sealed class Answer : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true}") });
    }
}
