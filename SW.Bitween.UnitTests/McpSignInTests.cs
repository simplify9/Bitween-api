using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SW.Bitween.Services;
using SW.Bitween.Services.Mcp;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SW.Bitween.UnitTests;

/// <summary>The edges of the AI apps' sign-in: when a code stops working, and what an app's metadata may be.</summary>
[TestClass]
public class McpSignInTests
{
    static readonly IConfiguration Configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string> { ["Token:Key"] = "unit-test-key-unit-test-key-unit-test-key" })
        .Build();

    [TestMethod]
    public void A_sign_in_code_stops_working_at_its_expiry_second()
    {
        var codes = new SignInCodes(Configuration);
        var verifier = new string('v', 43);
        var challenge = SignInCodes.Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var issued = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var code = codes.Issue(7, challenge, issued);
        var expiry = issued + SignInCodes.Lifetime;

        Assert.IsNotNull(codes.Redeem(code, verifier, expiry.AddMilliseconds(-1)));
        // Its record of use is cleared once past the expiry, so it can't still be good then.
        Assert.IsNull(codes.Redeem(code, verifier, expiry));
        Assert.IsNull(codes.Redeem(code, verifier, expiry.AddMilliseconds(500)));
    }

    [TestMethod]
    public async Task A_metadata_document_over_the_limit_is_refused_even_without_a_length()
    {
        const string clientId = "https://big.example.com/meta.json";
        var padding = new string(' ', 70 * 1024);
        OAuthClients.HandlerOverride = () => new Answering($$"""{"client_id":"{{clientId}}","redirect_uris":["https://big.example.com/cb"]}{{padding}}""");
        try
        {
            Assert.IsNull(await Clients().Find(clientId));
        }
        finally
        {
            OAuthClients.HandlerOverride = null;
        }
    }

    [TestMethod]
    public void An_app_named_after_its_address_gets_a_name_no_longer_than_any_other()
    {
        var host = new string('a', 63) + "." + new string('b', 63) + ".example.com";
        var client = Clients().Register(null, [$"https://{host}/cb"]);
        Assert.AreEqual(60, client.Name.Length);
    }

    static OAuthClients Clients() =>
        new(Configuration, new MemoryCache(new MemoryCacheOptions()), NullLogger<OAuthClients>.Instance);

    /// <summary>Answers with a body whose length isn't sent, as a chunked response's isn't.</summary>
    sealed class Answering(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(body)));
            content.Headers.ContentLength = null;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
}
