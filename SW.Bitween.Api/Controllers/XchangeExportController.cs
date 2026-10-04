using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.Bitween.Resources.Xchanges;
using SW.PrimitiveTypes;

namespace SW.Bitween.Controllers;

/// <summary>
/// The files of several exchanges as one zip, for the exchange list's "Export files": one folder per
/// exchange, named like its archive (<c>{main value}_{id}</c>), holding <c>input</c>, <c>mapped</c>
/// and <c>handled</c> — whichever it has. <see cref="Check"/> says beforehand how many exchanges are
/// older than storage keeps their files, so the person hears it before choosing to export. Files missing
/// anyway, or for another reason, are listed in <c>missing.txt</c>, so one of them never costs the rest.
/// </summary>
/// <remarks>
/// <para>
/// A controller rather than a CqApi handler, which can only return a file held whole in memory: this
/// streams each file from storage into the response as it's read. Its literal route outranks CqApi's
/// <c>{prefix}/{resource}/{token}</c>, the way <see cref="FileLinksController"/>'s does.
/// </para>
/// <para>
/// Refusals answer the way CqApi does — 401 without the permission, 400 with <c>{ code: [message] }</c>
/// — so the client reads them like any other call. They're all decided before the first byte is sent.
/// </para>
/// </remarks>
[ApiController]
[Route("api/xchanges/export")]
public class XchangeExportController(BitweenDbContext dbContext, RequestContext requestContext,
    XchangeService xchangeService, StorageRetention storageRetention, ILogger<XchangeExportController> logger)
    : ControllerBase
{
    /// <summary>
    /// The most exchanges one zip takes — the bulk retry's ceiling, for the same reason: every file is
    /// read from storage in sequence, inside the one request.
    /// </summary>
    public const int Limit = 500;

    public const string MissingList = "missing.txt";

    private static readonly Regex Extension = new(@"^\.[A-Za-z0-9]{1,10}$");

    /// <summary>
    /// What an export of <paramref name="request"/> would hold, asked before it's downloaded: how many
    /// exchanges it takes, and how many are older than the bucket's rule keeps their files. Refuses what
    /// the export itself would refuse.
    /// </summary>
    [HttpPost("check")]
    public async Task<IActionResult> Check([FromBody] XchangeFilesExport request)
    {
        var (ids, refusal) = await Resolve(request);
        if (refusal != null) return refusal;

        var xchanges = await dbContext.Set<Xchange>().AsNoTracking().Where(x => ids.Contains(x.Id)).ToListAsync();
        var rules = await storageRetention.GetAsync();
        var now = DateTime.UtcNow;
        // The days of the rule that deleted each exchange's files, for those it has.
        var deletedAfter = xchanges
            .Select(x => rules.RuleFor(xchangeService.FileKey(x, XchangeFileType.Input)) is { } rule &&
                         (now - x.StartedOn).TotalDays >= rule.Days ? rule.Days : (int?)null)
            .OfType<int>().ToList();
        var days = deletedAfter.Distinct().ToList();

        return Ok(new XchangeFilesExportCheck
        {
            Count = xchanges.Count,
            WithoutFiles = deletedAfter.Count,
            KeptDays = days.Count == 1 ? days[0] : null
        });
    }

    [HttpPost]
    public async Task<IActionResult> Post([FromBody] XchangeFilesExport request)
    {
        var (ids, refusal) = await Resolve(request);
        if (refusal != null) return refusal;

        var xchanges = await dbContext.Set<Xchange>().AsNoTracking()
            .Where(x => ids.Contains(x.Id)).OrderByDescending(x => x.StartedOn).ToListAsync();
        var results = await dbContext.Set<XchangeResult>().AsNoTracking()
            .Where(r => ids.Contains(r.Id)).ToDictionaryAsync(r => r.Id);
        var promoted = await dbContext.Set<XchangePromotedProperties>().AsNoTracking()
            .Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
        var documentIds = xchanges.Select(x => x.DocumentId).Distinct().ToList();
        var mainProperties = await dbContext.Set<Document>().AsNoTracking()
            .Where(d => documentIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, d => d.PromotedProperties?.Keys.FirstOrDefault());

        var missing = ids.Except(xchanges.Select(x => x.Id))
            .Select(id => $"{id}: this exchange no longer exists.").ToList();

        Response.ContentType = "application/zip";
        Response.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
        {
            FileName = $"exchanges-{DateTime.UtcNow.ToString("yyyy-MM-dd-HHmmss", CultureInfo.InvariantCulture)}.zip"
        }.ToString();

        var aborted = HttpContext.RequestAborted;
        var body = new HeldSyncWrites(Response.Body);
        await using (var zip = await ZipArchive.CreateAsync(body, ZipArchiveMode.Create, leaveOpen: true,
                         entryNameEncoding: null, aborted))
        {
            foreach (var xchange in xchanges)
            {
                var mainProperty = mainProperties.GetValueOrDefault(xchange.DocumentId);
                var mainValue = mainProperty == null ? null : promoted.GetValueOrDefault(xchange.Id)?.Properties?.GetValueOrDefault(mainProperty);
                var folder = ExchangeArchive.BaseName(mainValue, xchange.Id);

                await Add(zip, xchange, XchangeFileType.Input, $"{folder}/input",
                    xchange.InputName, xchange.InputSize, xchange.InputContentType, missing, aborted);

                if (!results.TryGetValue(xchange.Id, out var result)) continue;
                await Add(zip, xchange, XchangeFileType.Output, $"{folder}/mapped",
                    result.OutputName, result.OutputSize, result.OutputContentType, missing, aborted);
                await Add(zip, xchange, XchangeFileType.Response, $"{folder}/handled",
                    result.ResponseName, result.ResponseSize, result.ResponseContentType, missing, aborted);
            }

            if (missing.Count > 0)
            {
                await using var list = await zip.CreateEntry(MissingList).OpenAsync(aborted);
                await list.WriteAsync(Encoding.UTF8.GetBytes(
                    "These files of the exported exchanges aren't in this zip:\n\n" + string.Join("\n", missing) + "\n"), aborted);
            }
        }

        await body.FlushAsync(aborted);
        return new EmptyResult();
    }

    /// <summary>
    /// One file into the zip, named <paramref name="path"/> plus the extension its own name or content type
    /// gives. A file the exchange never had is left out; one storage no longer has goes on the missing list.
    /// </summary>
    private async Task Add(ZipArchive zip, Xchange xchange, XchangeFileType type, string path, string name, int size,
        string contentType, List<string> missing, CancellationToken aborted)
    {
        if (size == 0) return;
        path += ExtensionOf(name, contentType);

        Stream file;
        try
        {
            file = await xchangeService.OpenFile(xchange, type);
        }
        catch (SWValidationException gone)
        {
            missing.Add($"{path}: {gone.Validations.First().Value}");
            return;
        }
        catch (Exception) when (!aborted.IsCancellationRequested)
        {
            // Logged by OpenFile.
            missing.Add($"{path}: it couldn't be read from storage.");
            return;
        }

        await using (file)
        {
            var entry = zip.CreateEntry(path);
            await using var into = await entry.OpenAsync(aborted);
            try
            {
                await file.CopyToAsync(into, aborted);
            }
            catch (Exception ex) when (!aborted.IsCancellationRequested)
            {
                logger.LogError(ex, "Reading the {FileType} file of xchange {XchangeId} stopped part-way through an export.",
                    type, xchange.Id);
                missing.Add($"{path}: storage stopped sending it part-way, so the copy in this zip is incomplete.");
            }
        }
    }

    /// <summary>The file's own extension when it has a plain one, or else one for its content type.</summary>
    private static string ExtensionOf(string name, string contentType)
    {
        var own = Path.GetExtension(name ?? string.Empty);
        if (Extension.IsMatch(own)) return own.ToLowerInvariant();

        return (contentType ?? string.Empty).ToLowerInvariant() switch
        {
            var type when type.Contains("json") => ".json",
            var type when type.Contains("xml") => ".xml",
            var type when type.Contains("csv") => ".csv",
            var type when type.StartsWith("text/") => ".txt",
            _ => string.Empty
        };
    }

    /// <summary>The exchanges <paramref name="request"/> selects, or why it can't be exported.</summary>
    private async Task<(List<string> Ids, IActionResult Refusal)> Resolve(XchangeFilesExport request)
    {
        try
        {
            await requestContext.EnsurePermission(dbContext, Model.Permissions.Exchanges.View);
        }
        catch (SWUnauthorizedException)
        {
            return (null, Unauthorized());
        }

        var selection = new XchangeSelection(dbContext);
        var ids = await selection.Resolve(request, Limit);
        if (ids.Count == 0) return (null, Refuse("NOTHING_SELECTED", "Select at least one exchange to export."));
        if (ids.Count > Limit)
        {
            var count = string.IsNullOrWhiteSpace(request.Filter) ? ids.Count : await selection.Count(request);
            return (null, Refuse("TOO_MANY",
                $"{count:n0} exchanges is more than the {Limit} one export can take. Narrow the filter and export the rest after."));
        }

        return (ids, null);
    }

    private BadRequestObjectResult Refuse(string code, string message)
    {
        ModelState.AddModelError(code, message);
        return BadRequest(ModelState);
    }

    /// <summary>
    /// The response body, for the zip. Even through its async methods the zip closes each entry with a
    /// synchronous write, which the response body refuses: those few bytes are held here and go out with
    /// the next asynchronous write, so nothing blocks a thread and the file contents still stream.
    /// </summary>
    private sealed class HeldSyncWrites(Stream response) : Stream
    {
        private readonly MemoryStream _held = new();

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count) => _held.Write(buffer, offset, count);
        public override void Write(ReadOnlySpan<byte> buffer) => _held.Write(buffer);

        /// <summary>Nothing to do until the next asynchronous write or flush.</summary>
        public override void Flush()
        {
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await SendHeld(cancellationToken);
            await response.WriteAsync(buffer, cancellationToken);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            await SendHeld(cancellationToken);
            await response.FlushAsync(cancellationToken);
        }

        private async Task SendHeld(CancellationToken cancellationToken)
        {
            if (_held.Length == 0) return;
            await response.WriteAsync(_held.GetBuffer().AsMemory(0, (int)_held.Length), cancellationToken);
            _held.SetLength(0);
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
