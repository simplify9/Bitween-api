#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SW.Bitween.NativeAdapters.Mapper;
using SW.Bitween.NativeAdapters.Mapper.Formats;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.MappingPreviews;

public class MappingPreviewRequest
{
    /// <summary>The rules as the editor holds them, serialised.</summary>
    public string MappingRules { get; set; } = "";

    /// <summary>The sample document to map.</summary>
    public string SourceDocument { get; set; } = "";

    /// <summary>Whose partner values to map against. Null maps with none.</summary>
    public int? PartnerId { get; set; }
}

public class MappingPreviewResponse
{
    /// <summary>The mapped document, or null when the mapping could not run.</summary>
    public string? OutputDocument { get; set; }

    /// <summary>Content type of <see cref="OutputDocument"/>, so the editor can label it.</summary>
    public string? ContentType { get; set; }

    /// <summary>
    /// One entry per rule that could not be applied, so the editor can mark each one.
    /// </summary>
    public List<MappingPreviewError> RuleErrors { get; set; } = new();

    /// <summary>Set when the mapping could not be attempted at all - unreadable document or rules.</summary>
    public string? Error { get; set; }
}

public class MappingPreviewError
{
    public string Target { get; set; } = "";
    public string Reason { get; set; } = "";
}

/// <summary>
/// Maps a sample document with unsaved rules, so the editor can show the result as it is edited.
/// </summary>
/// <remarks>
/// <para>
/// Runs the same three steps the exchange pipeline runs - read, map, write - against the same
/// context factory. That is deliberate and it is the point: the old preview generated its own
/// template and assembled its own partner values, and the two drifted, so a mapping that previewed
/// correctly could fail in production.
/// </para>
/// <para>
/// Nothing here is saved. The rules arrive in the request because the editor has not committed them
/// yet.
/// </para>
/// </remarks>
public class Preview(RequestContext requestContext, BitweenDbContext dbContext,
        MappingContextFactory contextFactory)
    : ICommandHandler<MappingPreviewRequest, MappingPreviewResponse>
{
    public async Task<MappingPreviewResponse> Handle(MappingPreviewRequest request)
    {
        // The editor opens while creating a subscription as well as while editing one, so either
        // right will do. Without a guard any signed-in token could map any partner's values.
        await requestContext.EnsurePermission(dbContext,
            Model.Permissions.Subscriptions.Create, Model.Permissions.Subscriptions.Edit);

        MappingRules rules;
        try
        {
            rules = JsonConvert.DeserializeObject<MappingRules>(request.MappingRules ?? "")
                    ?? new MappingRules();
        }
        catch (JsonException ex)
        {
            return new MappingPreviewResponse { Error = $"The mapping rules could not be read: {ex.Message}" };
        }

        if (rules.Version > MappingRules.CurrentVersion)
            return new MappingPreviewResponse
            {
                Error = $"These mapping rules are version {rules.Version}, but this version of " +
                        $"Bitween understands up to version {MappingRules.CurrentVersion}.",
            };

        if (SourceFormat(rules, request.SourceDocument) is not { } source)
            return new MappingPreviewResponse { Error = DocumentFormats.Unsupported(rules.SourceFormat, "source") };

        if (TargetFormat(rules) is not { } target)
            return new MappingPreviewResponse { Error = DocumentFormats.Unsupported(rules.TargetFormat, "target") };

        ValueNode input;
        try
        {
            input = source.Read(request.SourceDocument ?? "");
        }
        catch (DocumentFormatException ex)
        {
            return new MappingPreviewResponse { Error = ex.Message };
        }

        var context = await contextFactory.Build(request.PartnerId, maskSecrets: true);

        try
        {
            var output = DocumentMapper.Map(rules, input, context, source.SingleValueIsAList);
            return new MappingPreviewResponse
            {
                OutputDocument = target.Write(output),
                ContentType = target.ContentType,
            };
        }
        catch (MappingFailedException ex)
        {
            // Reported per rule rather than as one message, so the editor can mark the rows that
            // are wrong instead of showing a wall of text above the document.
            return new MappingPreviewResponse
            {
                RuleErrors = ex.Errors
                    .Select(e => new MappingPreviewError { Target = e.Target, Reason = e.Reason })
                    .ToList(),
            };
        }
        catch (DocumentFormatException ex)
        {
            // The writer can refuse what the rules produced: XML has exactly one root element and
            // no way to repeat it, where JSON is happy with anything. It belongs to the document
            // rather than to any one rule, so it is reported the same way a bad sample is.
            return new MappingPreviewResponse { Error = ex.Message };
        }
    }

    /// <summary>What reads an editor's source sample, or null for a format no reader answers to.</summary>
    internal static IDocumentFormat? SourceFormat(MappingRules rules, string? sample)
    {
        if (!DocumentFormats.TryGet(rules.SourceFormat, out var source, rules.SourceOptions())) return null;

        // A workbook can't be pasted into the editor, so its sample is cells copied from Excel,
        // which arrive as tab-separated text. Read that way when it isn't a workbook in base64;
        // the exchange pipeline only ever reads the workbook itself.
        return source is ExcelFormat && !LooksLikeBase64(sample ?? "")
            ? new CsvFormat(new CsvOptions { Delimiter = "\t", HasHeader = rules.SourceExcel?.HasHeader != false })
            : source;
    }

    /// <summary>What writes the preview's output, and reads an editor's output sample.</summary>
    internal static IDocumentFormat? TargetFormat(MappingRules rules)
    {
        if (!DocumentFormats.TryGet(rules.TargetFormat, out var target, rules.TargetOptions())) return null;

        // And a workbook can't be shown, so the preview shows its rows the same way.
        return target is ExcelFormat
            ? new CsvFormat(new CsvOptions { Delimiter = "\t", HasHeader = rules.TargetExcel?.HasHeader != false })
            : target;
    }

    static bool LooksLikeBase64(string text)
    {
        var trimmed = (text ?? "").Trim();
        if (trimmed.Length == 0 || trimmed.Length % 4 != 0) return false;
        Span<byte> buffer = new byte[trimmed.Length];
        return Convert.TryFromBase64String(trimmed, buffer, out _);
    }
}
