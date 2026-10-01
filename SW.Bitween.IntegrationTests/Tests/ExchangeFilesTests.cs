using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SW.Bitween.Controllers;
using SW.Bitween.Domain;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// Where an exchange's files live, who can read them, and what a reader is told when one is gone.
/// </summary>
[Collection("Bitween")]
public class ExchangeFilesTests(BitweenFixture fixture) : IAsyncLifetime
{
    private readonly BitweenOptions _options = fixture.App.Services.GetRequiredService<BitweenOptions>();
    private string _prefix;
    private string _legacyPrefix;

    /// <summary>The options object is shared by the whole collection, so what these tests change goes back.</summary>
    public Task InitializeAsync()
    {
        _prefix = _options.DocumentPrefix;
        _legacyPrefix = _options.LegacyDocumentPrefix;
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _options.DocumentPrefix = _prefix;
        _options.LegacyDocumentPrefix = _legacyPrefix;
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Files_stay_readable_after_the_document_prefix_changes()
    {
        _options.DocumentPrefix = $"temp30/{Unique("before")}";
        var written = await CreateExchange("{\"order\":\"A-1\"}");

        _options.DocumentPrefix = $"temp7/{Unique("after")}";

        await using var scope = fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<XchangeService>();

        // Recorded on the exchange, so the file is still found where it was written...
        Assert.StartsWith("temp30/", written.FilesPrefix);
        Assert.Equal("{\"order\":\"A-1\"}", await service.GetFile(written.Id, XchangeFileType.Input));

        // ...and only exchanges created after the change go under the new prefix.
        var later = await CreateExchange("{\"order\":\"A-2\"}");
        Assert.Equal($"{_options.DocumentPrefix}/{later.Id}/input", service.FileKey(later, XchangeFileType.Input));
    }

    [Fact]
    public async Task An_exchange_from_before_prefixes_were_recorded_reads_from_the_legacy_prefix()
    {
        _options.DocumentPrefix = $"temp30/{Unique("legacy")}";
        var old = await CreateExchange("{\"order\":\"L-1\"}");
        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            await db.Set<Xchange>().Where(x => x.Id == old.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.FilesPrefix, (string)null));
        }

        _options.LegacyDocumentPrefix = _options.DocumentPrefix;
        _options.DocumentPrefix = $"temp7/{Unique("moved-on")}";

        await using var readScope = fixture.CreateScope();
        var service = readScope.ServiceProvider.GetRequiredService<XchangeService>();
        Assert.Equal("{\"order\":\"L-1\"}", await service.GetFile(old.Id, XchangeFileType.Input));
    }

    [Fact]
    public async Task Only_a_key_that_is_exactly_an_exchange_file_has_an_owner()
    {
        var xchange = await CreateExchange("{}");
        await using var scope = fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<XchangeService>();
        var key = service.FileKey(xchange, XchangeFileType.Input);

        var owner = await service.FindFileOwner(key);
        Assert.Equal(xchange.Id, owner?.Xchange.Id);
        Assert.Equal(XchangeFileType.Input, owner?.Type);

        // The same exchange under another prefix, another file name, or a key that isn't an
        // exchange's at all: the endpoint that reads by key must refuse every one of these.
        Assert.Null(await service.FindFileOwner($"temp1/elsewhere/{xchange.Id}/input"));
        Assert.Null(await service.FindFileOwner($"{xchange.FilesPrefix}/{xchange.Id}/secrets"));
        Assert.Null(await service.FindFileOwner("adapters/sw.bitween.samplehandler"));
        Assert.Null(await service.FindFileOwner(null));
    }

    [Fact]
    public async Task Reading_a_file_needs_the_exchanges_view()
    {
        var xchange = await CreateExchange("{\"secret\":\"payload\"}");
        await using var scope = fixture.CreateScope();
        scope.AsAnonymous();
        var key = scope.ServiceProvider.GetRequiredService<XchangeService>().FileKey(xchange, XchangeFileType.Input);
        var handler = ActivatorUtilities.CreateInstance<Resources.BitweenDocs.Get>(scope.ServiceProvider);

        await Assert.ThrowsAsync<SWUnauthorizedException>(() =>
            handler.Handle(new GetBitweenDocModel { DocumentKey = key }));
    }

    [Fact]
    public async Task The_docs_endpoint_reads_exchange_files_and_nothing_else_in_the_bucket()
    {
        var xchange = await CreateExchange("{\"visible\":true}");
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var key = scope.ServiceProvider.GetRequiredService<XchangeService>().FileKey(xchange, XchangeFileType.Input);
        var handler = ActivatorUtilities.CreateInstance<Resources.BitweenDocs.Get>(scope.ServiceProvider);

        var read = await handler.Handle(new GetBitweenDocModel { DocumentKey = key });
        Assert.Equal("{\"visible\":true}", read.GetType().GetProperty("Data")!.GetValue(read));

        // Adapters live in the same bucket; this used to hand any of them to anyone who asked.
        await Assert.ThrowsAsync<SWNotFoundException>(() =>
            handler.Handle(new GetBitweenDocModel { DocumentKey = "adapters/sw.bitween.samplehandler" }));
    }

    [Fact]
    public async Task A_file_the_bucket_expired_is_reported_as_deleted_by_the_retention_policy()
    {
        _options.DocumentPrefix = $"temp30/{Unique("expired")}";
        var expired = await CreateExchange("{}");
        var recent = await CreateExchange("{}");
        await Backdate(expired.Id, days: 45);

        await using var scope = fixture.CreateScope();
        var cloudFiles = scope.ServiceProvider.GetRequiredService<ICloudFilesService>();
        var service = ActivatorUtilities.CreateInstance<XchangeService>(scope.ServiceProvider,
            RetentionOver(new CloudFilesLifecycleRule { Id = "temp30", Prefix = "temp30/", Days = 30, Enabled = true }));

        await cloudFiles.DeleteAsync(service.FileKey(expired, XchangeFileType.Input));
        await cloudFiles.DeleteAsync(service.FileKey(recent, XchangeFileType.Input));

        var gone = await Assert.ThrowsAsync<SWValidationException>(() =>
            service.GetFile(expired.Id, XchangeFileType.Input));
        Assert.Contains("FILE_EXPIRED", gone.Message);
        Assert.Contains("kept 30 days", gone.Message);

        // Younger than the rule: the rule can't be what removed it, so it isn't blamed.
        var missing = await Assert.ThrowsAsync<SWValidationException>(() =>
            service.GetFile(recent.Id, XchangeFileType.Input));
        Assert.Contains("FILE_MISSING", missing.Message);
    }

    [Fact]
    public async Task A_file_link_opens_its_own_file_without_a_login_and_no_other()
    {
        var mine = await CreateExchange("{\"link\":\"mine\"}");
        var other = await CreateExchange("{\"link\":\"other\"}");

        await using var scope = fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<XchangeService>();
        var links = scope.ServiceProvider.GetRequiredService<FileLinks>();
        var controller = ActivatorUtilities.CreateInstance<FileLinksController>(scope.ServiceProvider);

        var url = service.FileUrl(mine.Id, mine.FilesPrefix, XchangeFileType.Input);
        var mineKey = service.FileKey(mine, XchangeFileType.Input);
        Assert.StartsWith($"https://bitween.test/{FileLinks.RoutePrefix}/", url);
        // The storage key is the tail of the link, as it was the tail of the storage URL.
        Assert.EndsWith($"/{mineKey}", url);

        var seal = url[$"https://bitween.test/{FileLinks.RoutePrefix}/".Length..].Split('/')[0];
        var opened = Assert.IsType<FileStreamResult>(await controller.Get(seal, mineKey));
        Assert.Equal("text/plain", opened.ContentType);
        using (var reader = new StreamReader(opened.FileStream))
            Assert.Equal("{\"link\":\"mine\"}", await reader.ReadToEndAsync());

        // The seal belongs to one key: moved onto another exchange's file, or altered, it opens nothing.
        Assert.IsType<NotFoundResult>(await controller.Get(seal, service.FileKey(other, XchangeFileType.Input)));
        Assert.IsType<NotFoundResult>(await controller.Get(seal[..^1] + (seal[^1] == 'A' ? 'B' : 'A'), mineKey));
        Assert.False(links.Opens(seal, service.FileKey(mine, XchangeFileType.Output)));
    }

    private async Task<Xchange> CreateExchange(string payload)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var document = new Document(null, Unique("Files type"), DocumentFormat.Json);
        db.Add(document);
        await db.SaveChangesAsync();
        var xchange = await scope.ServiceProvider.GetRequiredService<XchangeService>()
            .CreateXchange(document, null, new XchangeFile(payload));
        await db.SaveChangesAsync();
        return xchange;
    }

    private async Task Backdate(string xchangeId, int days)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var startedOn = DateTime.UtcNow.AddDays(-days);
        await db.Set<Xchange>().Where(x => x.Id == xchangeId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.StartedOn, startedOn));
    }

    /// <summary>The local test storage has no deletion rules; this stands in for a bucket that does.</summary>
    internal static StorageRetention RetentionOver(params CloudFilesLifecycleRule[] rules) =>
        new(new ServiceCollection()
                .AddSingleton<ICloudFilesLifecycle>(new FixedLifecycle(rules))
                .BuildServiceProvider(),
            NullLogger<StorageRetention>.Instance);

    private static string Unique(string label) => $"{label}-{Guid.NewGuid():N}"[..20];

    internal sealed class FixedLifecycle(CloudFilesLifecycleRule[] rules) : ICloudFilesLifecycle
    {
        public Task<CloudFilesLifecycle> GetLifecycleAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new CloudFilesLifecycle { Provider = "Test", Bucket = "test", Rules = rules });
    }
}
