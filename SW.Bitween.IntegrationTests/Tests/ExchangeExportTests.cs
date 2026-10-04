using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Controllers;
using SW.Bitween.Domain;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// The exchange list's "Export files": the selected exchanges' files as one zip, a folder per exchange.
/// </summary>
[Collection("Bitween")]
public class ExchangeExportTests(BitweenFixture fixture) : IAsyncLifetime
{
    private readonly BitweenOptions _options = fixture.App.Services.GetRequiredService<BitweenOptions>();
    private string _prefix;

    /// <summary>The options object is shared by the whole collection, so what these tests change goes back.</summary>
    public Task InitializeAsync()
    {
        _prefix = _options.DocumentPrefix;
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _options.DocumentPrefix = _prefix;
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Each_exchange_gets_a_folder_named_like_its_archive_with_the_files_it_has()
    {
        var document = await InformationType();
        var full = await CreateExchange(document, "{\"city\":\"Amman\"}", "orders.json", city: "Amman",
            output: new XchangeFile("{\"mapped\":true}", "orders.json"),
            response: new XchangeFile("accepted") { ContentType = "text/plain" });
        var inputOnly = await CreateExchange(document, "{\"city\":null}", "intake.csv");

        // A viewer: reading files is all an export does.
        await using var scope = fixture.CreateScope();
        await scope.AsNewViewer(Unique("export-viewer"));
        var (result, zip, response) = await Export(scope, new XchangeFilesExport { Ids = [full.Id, inputOnly.Id] });

        Assert.IsType<EmptyResult>(result);
        Assert.Equal("application/zip", response.ContentType);
        Assert.Matches(@"^attachment; filename=exchanges-\d{4}-\d{2}-\d{2}-\d{6}\.zip$", response.Headers.ContentDisposition.ToString());
        Assert.Equal(new Dictionary<string, string>
        {
            [$"Amman_{full.Id}/input.json"] = "{\"city\":\"Amman\"}",
            [$"Amman_{full.Id}/mapped.json"] = "{\"mapped\":true}",
            [$"Amman_{full.Id}/handled.txt"] = "accepted",
            [$"{inputOnly.Id}/input.csv"] = "{\"city\":null}",
        }, zip);
    }

    [Fact]
    public async Task A_file_storage_no_longer_has_is_listed_in_missing_txt_and_the_rest_still_come()
    {
        _options.DocumentPrefix = $"temp30/{Unique("export")}";
        var document = await InformationType();
        var expired = await CreateExchange(document, "{\"old\":true}", "old.json");
        var kept = await CreateExchange(document, "{\"new\":true}", "new.json");
        await Backdate(expired.Id, days: 45);

        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var service = ActivatorUtilities.CreateInstance<XchangeService>(scope.ServiceProvider,
            ExchangeFilesTests.RetentionOver(new CloudFilesLifecycleRule { Id = "temp30", Prefix = "temp30/", Days = 30, Enabled = true }));
        await scope.ServiceProvider.GetRequiredService<ICloudFilesService>()
            .DeleteAsync(service.FileKey(expired, XchangeFileType.Input));
        var gone = Guid.NewGuid().ToString("N");

        var (_, zip, _) = await Export(scope, new XchangeFilesExport { Ids = [expired.Id, kept.Id, gone] }, service);

        Assert.Equal(new[] { "missing.txt", $"{kept.Id}/input.json" }.Order(), zip.Keys.Order());
        Assert.Equal("{\"new\":true}", zip[$"{kept.Id}/input.json"]);
        var missing = zip["missing.txt"];
        Assert.Contains($"{expired.Id}/input.json: This file was deleted by the storage retention policy: " +
                        "files under temp30/ are kept 30 days", missing);
        Assert.Contains($"{gone}: this exchange no longer exists.", missing);
    }

    [Fact]
    public async Task Before_the_download_it_says_how_many_exchanges_are_older_than_storage_keeps_their_files()
    {
        _options.DocumentPrefix = $"temp30/{Unique("check")}";
        var document = await InformationType();
        var expired = await CreateExchange(document, "{\"old\":true}", "old.json");
        var kept = await CreateExchange(document, "{\"new\":true}", "new.json");
        await Backdate(expired.Id, days: 45);

        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var controller = ActivatorUtilities.CreateInstance<XchangeExportController>(scope.ServiceProvider,
            ExchangeFilesTests.RetentionOver(new CloudFilesLifecycleRule { Id = "temp30", Prefix = "temp30/", Days = 30, Enabled = true }));

        var check = Assert.IsType<XchangeFilesExportCheck>(Assert.IsType<OkObjectResult>(
            await controller.Check(new XchangeFilesExport { Ids = [expired.Id, kept.Id] })).Value);
        Assert.Equal(2, check.Count);
        Assert.Equal(1, check.WithoutFiles);
        Assert.Equal(30, check.KeptDays);

        // Refused the way the export itself would be.
        Assert.IsType<BadRequestObjectResult>(await controller.Check(new XchangeFilesExport { Ids = [] }));
    }

    [Fact]
    public async Task Select_all_matching_exports_what_the_filter_matches_minus_the_unticked_rows()
    {
        var document = await InformationType();
        var first = await CreateExchange(document, "{\"n\":1}", "1.json");
        var unticked = await CreateExchange(document, "{\"n\":2}", "2.json");
        var third = await CreateExchange(document, "{\"n\":3}", "3.json");

        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var (_, zip, _) = await Export(scope,
            new XchangeFilesExport { Filter = $"filter=DocumentId:1:{document.Id}", ExcludeIds = [unticked.Id] });

        Assert.Equal(new[] { $"{first.Id}/input.json", $"{third.Id}/input.json" }.Order(), zip.Keys.Order());
    }

    [Fact]
    public async Task A_selection_past_the_limit_or_an_empty_one_is_refused_before_anything_is_sent()
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var tooMany = Enumerable.Range(0, XchangeExportController.Limit + 1).Select(_ => Guid.NewGuid().ToString("N")).ToList();

        var (result, zip, _) = await Export(scope, new XchangeFilesExport { Ids = tooMany });
        var refused = Assert.IsType<BadRequestObjectResult>(result);
        var errors = Assert.IsType<SerializableError>(refused.Value);
        Assert.Equal(["501 exchanges is more than the 500 one export can take. Narrow the filter and export the rest after."],
            (string[])errors["TOO_MANY"]);
        Assert.Empty(zip);

        var (empty, _, _) = await Export(scope, new XchangeFilesExport { Ids = [] });
        Assert.True(Assert.IsType<SerializableError>(Assert.IsType<BadRequestObjectResult>(empty).Value).ContainsKey("NOTHING_SELECTED"));
    }

    [Fact]
    public async Task Exporting_needs_the_exchanges_view()
    {
        var document = await InformationType();
        var xchange = await CreateExchange(document, "{\"secret\":true}", "secret.json");

        await using var scope = fixture.CreateScope();
        scope.AsAnonymous();
        var (result, zip, _) = await Export(scope, new XchangeFilesExport { Ids = [xchange.Id] });

        Assert.IsType<UnauthorizedResult>(result);
        Assert.Empty(zip);
    }

    /// <summary>
    /// Runs the export into a body that, like Kestrel's, refuses synchronous writes, and reads back the
    /// zip's entries: name to text. Empty when nothing was written.
    /// </summary>
    private static async Task<(IActionResult Result, Dictionary<string, string> Zip, HttpResponse Response)> Export(
        AsyncServiceScope scope, XchangeFilesExport request, XchangeService service = null)
    {
        var controller = service == null
            ? ActivatorUtilities.CreateInstance<XchangeExportController>(scope.ServiceProvider)
            : ActivatorUtilities.CreateInstance<XchangeExportController>(scope.ServiceProvider, service);
        var body = new ResponseBody();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        http.Response.Body = body;
        controller.ControllerContext = new ControllerContext { HttpContext = http };

        var result = await controller.Post(request);

        var zip = new Dictionary<string, string>();
        if (body.Written.Length == 0) return (result, zip, http.Response);

        using var archive = new ZipArchive(new MemoryStream(body.Written), ZipArchiveMode.Read);
        foreach (var entry in archive.Entries)
        {
            using var reader = new StreamReader(entry.Open());
            zip[entry.FullName] = await reader.ReadToEndAsync();
        }

        return (result, zip, http.Response);
    }

    private async Task<Document> InformationType()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var document = new Document(null, Unique("Export type"), DocumentFormat.Json);
        // "city" first: the main property, which leads each exchange's folder name.
        document.SetDictionaries(new Dictionary<string, string> { ["city"] = "$.city", ["order"] = "$.order" });
        db.Add(document);
        await db.SaveChangesAsync();
        return document;
    }

    private async Task<Xchange> CreateExchange(Document document, string payload, string fileName, string city = null,
        XchangeFile output = null, XchangeFile response = null)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<XchangeService>();
        var xchange = await service.CreateXchange(document, null, new XchangeFile(payload, fileName));

        if (city != null)
        {
            var filtered = new FilterResult();
            filtered.Properties["order"] = "PO-1";
            filtered.Properties["city"] = city;
            db.Add(new XchangePromotedProperties(xchange.Id, filtered));
        }

        if (output != null || response != null)
        {
            var cloudFiles = scope.ServiceProvider.GetRequiredService<ICloudFilesService>();
            if (output != null)
                await cloudFiles.WriteTextAsync(output.Data, new WriteFileSettings { Key = service.FileKey(xchange, XchangeFileType.Output) });
            if (response != null)
                await cloudFiles.WriteTextAsync(response.Data, new WriteFileSettings { Key = service.FileKey(xchange, XchangeFileType.Response) });
            db.Add(new XchangeResult(xchange.Id, null, output, response));
        }

        await db.SaveChangesAsync();
        return xchange;
    }

    private async Task Backdate(string xchangeId, int days)
    {
        await using var scope = fixture.CreateScope();
        var startedOn = DateTime.UtcNow.AddDays(-days);
        await scope.ServiceProvider.GetRequiredService<BitweenDbContext>().Set<Xchange>()
            .Where(x => x.Id == xchangeId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.StartedOn, startedOn));
    }

    private static string Unique(string label) => $"{label}-{Guid.NewGuid():N}"[..20];

    /// <summary>A response body as Kestrel serves one: write-only, not seekable, and async only.</summary>
    private sealed class ResponseBody : Stream
    {
        private readonly MemoryStream _written = new();

        public byte[] Written => _written.ToArray();

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count) => throw Synchronous();
        public override void Flush() => throw Synchronous();

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            _written.Write(buffer, offset, count);
            return Task.CompletedTask;
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            _written.Write(buffer.Span);
            return ValueTask.CompletedTask;
        }

        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        private static InvalidOperationException Synchronous() => new("Synchronous operations are disallowed.");
    }
}
