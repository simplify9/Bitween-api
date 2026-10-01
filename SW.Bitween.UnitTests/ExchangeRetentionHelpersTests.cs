using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SW.Bitween.UnitTests;

/// <summary>The pure parts of retention: archive names, missing-file detection and file link seals.</summary>
[TestClass]
public class ExchangeRetentionHelpersTests
{
    [TestMethod]
    public void The_archive_sits_outside_the_temp_folders_but_keeps_the_rest_of_the_prefix()
    {
        Assert.AreEqual("archive/infolinkdocs", ExchangeArchive.PrefixFor("temp30/infolinkdocs"));
        Assert.AreEqual("archive/docs/eu", ExchangeArchive.PrefixFor("temp365/docs/eu"));
        Assert.AreEqual("archive/docs", ExchangeArchive.PrefixFor("docs"));
        Assert.AreEqual("archive", ExchangeArchive.PrefixFor("temp7"));
    }

    [TestMethod]
    public void An_archive_is_named_by_its_main_value_made_safe_then_its_id()
    {
        Assert.AreEqual("ORD-1001-A_abc.json", ExchangeArchive.FileName("ORD 1001/A", "abc"));
        Assert.AreEqual("abc.json", ExchangeArchive.FileName(null, "abc"));
        Assert.AreEqual("abc.json", ExchangeArchive.FileName("  //  ", "abc"));

        var longValue = new string('x', 200);
        Assert.AreEqual(80 + "_abc.json".Length, ExchangeArchive.FileName(longValue, "abc").Length);
    }

    [TestMethod]
    public void Not_found_is_recognised_whichever_provider_threw_it()
    {
        Assert.IsTrue(StorageErrors.IsNotFound(new FileNotFoundException()));
        Assert.IsTrue(StorageErrors.IsNotFound(new WithStatusCode(HttpStatusCode.NotFound)));
        Assert.IsTrue(StorageErrors.IsNotFound(new WithStatus(404)));
        Assert.IsTrue(StorageErrors.IsNotFound(new InvalidOperationException("wrapped", new WithStatus(404))));

        Assert.IsFalse(StorageErrors.IsNotFound(new WithStatusCode(HttpStatusCode.Forbidden)));
        Assert.IsFalse(StorageErrors.IsNotFound(new TimeoutException()));
    }

    [TestMethod]
    public void A_file_link_seal_fits_its_key_and_no_other()
    {
        var links = Links("a-signing-key-that-is-long-enough-0123456789");
        var url = links.LinkTo("temp30/docs/abc/input");
        var seal = url["https://bitween.test/api/files/".Length..].Split('/')[0];

        Assert.IsTrue(links.Opens(seal, "temp30/docs/abc/input"));
        Assert.IsFalse(links.Opens(seal, "temp30/docs/abd/input"));
        Assert.IsFalse(links.Opens(seal, "temp30/docs/abc/output"));
        Assert.IsFalse(links.Opens("", "temp30/docs/abc/input"));
        // Another deployment's key can't make a seal this one accepts.
        Assert.IsFalse(Links("another-signing-key-that-is-long-enough-987").Opens(seal, "temp30/docs/abc/input"));
    }

    [TestMethod]
    public void Without_a_public_address_or_a_request_links_go_to_the_instance_where_its_adapters_reach_it()
    {
        Assert.AreEqual("http://localhost:8080", InstanceLinks(null, "http://[::]:8080").BaseUrl);
        Assert.AreEqual("http://localhost:80", InstanceLinks(null, "http://+:80").BaseUrl);
        Assert.AreEqual("http://localhost:8080", InstanceLinks(null, "http://0.0.0.0:8080").BaseUrl);
        Assert.AreEqual("http://10.0.0.5:8080", InstanceLinks(null, "http://10.0.0.5:8080").BaseUrl);
        // Plain HTTP when there's a choice: no certificate for the adapter to trust.
        Assert.AreEqual("http://localhost:5155", InstanceLinks(null, "https://localhost:7155", "http://localhost:5155").BaseUrl);
        Assert.AreEqual("https://localhost:7155", InstanceLinks(null, "https://localhost:7155").BaseUrl);
        // Resident adapters talk to the host over a Unix socket on the same server; it's no address for a link.
        Assert.AreEqual("http://localhost:8080", InstanceLinks(null, "http://unix:/tmp/swsl-1.sock", "http://[::]:8080").BaseUrl);
        Assert.AreEqual("https://localhost:7155", InstanceLinks(null, "http://unix:/tmp/swsl-1.sock", "https://localhost:7155").BaseUrl);

        Assert.AreEqual("https://bitween.test", InstanceLinks("https://bitween.test/", "http://[::]:8080").BaseUrl);
        Assert.IsNull(InstanceLinks(null).BaseUrl);
    }

    [TestMethod]
    public void Links_for_adapters_never_use_the_address_of_whoever_made_the_request()
    {
        var accessor = new Microsoft.AspNetCore.Http.HttpContextAccessor
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
            {
                Request = { Scheme = "https", Host = new Microsoft.AspNetCore.Http.HostString("admin.vpn.example") }
            }
        };
        var links = new FileLinks(new BitweenOptions(), new ConfigurationBuilder().Build(), accessor,
            new ListeningOn(["http://[::]:8080"]));

        Assert.AreEqual("https://admin.vpn.example", links.BaseUrl);
        Assert.AreEqual("http://localhost:8080", links.AdapterBaseUrl);
        StringAssert.StartsWith(links.LinkTo("temp30/docs/abc/input", forAdapter: true), "http://localhost:8080/api/files/");
    }

    [TestMethod]
    public void The_storage_counts_as_open_only_when_the_file_itself_comes_back_without_credentials()
    {
        Assert.AreEqual(true, StorageAccess.Opens(HttpStatusCode.OK, StorageAccess.ProbeText));
        Assert.AreEqual(false, StorageAccess.Opens(HttpStatusCode.Forbidden, "<Error>AccessDenied</Error>"));
        Assert.AreEqual(false, StorageAccess.Opens(HttpStatusCode.NotFound, ""));
        Assert.AreEqual(false, StorageAccess.Opens(HttpStatusCode.Unauthorized, ""));
        // A sign-in page or a proxy's error isn't the file: no verdict either way.
        Assert.IsNull(StorageAccess.Opens(HttpStatusCode.OK, "<html>Sign in</html>"));
        Assert.IsNull(StorageAccess.Opens(HttpStatusCode.BadGateway, ""));
    }

    private static FileLinks Links(string tokenKey) => new(
        new BitweenOptions { PublicUrl = "https://bitween.test/" },
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string> { ["Token:Key"] = tokenKey }).Build(),
        new Microsoft.AspNetCore.Http.HttpContextAccessor());

    private static FileLinks InstanceLinks(string publicUrl, params string[] listeningOn) => new(
        new BitweenOptions { PublicUrl = publicUrl },
        new ConfigurationBuilder().Build(),
        new Microsoft.AspNetCore.Http.HttpContextAccessor(),
        new ListeningOn(listeningOn));

    private sealed class ListeningOn : IServer
    {
        public ListeningOn(string[] addresses)
        {
            var feature = new ServerAddressesFeature();
            foreach (var address in addresses) feature.Addresses.Add(address);
            Features.Set<IServerAddressesFeature>(feature);
        }

        public IFeatureCollection Features { get; } = new FeatureCollection();

        public Task StartAsync<TContext>(IHttpApplication<TContext> application, CancellationToken cancellationToken)
            where TContext : notnull => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void Dispose() { }
    }

    private sealed class WithStatusCode(HttpStatusCode statusCode) : Exception
    {
        public HttpStatusCode StatusCode { get; } = statusCode;
    }

    private sealed class WithStatus(int status) : Exception
    {
        public int Status { get; } = status;
    }
}
