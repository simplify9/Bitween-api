#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SW.Bitween.NativeAdapters.Mapper;
using SW.Bitween.NativeAdapters.Mapper.Formats;
using SW.Bitween.Resources.MappingPreviews;
using SW.Bitween.Services.Ai;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.MappingSuggestions;

public class MappingSuggestionRequest
{
    /// <summary>
    /// The rules in the editor, serialised. Their formats are always kept; with <see cref="Refine"/>
    /// they are also what gets fixed.
    /// </summary>
    public string MappingRules { get; set; } = "";

    public string SourceDocument { get; set; } = "";

    /// <summary>What <see cref="SourceDocument"/> should become.</summary>
    public string TargetDocument { get; set; } = "";

    /// <summary>Whose partner values the check maps against, as for a preview.</summary>
    public int? PartnerId { get; set; }

    /// <summary>Fix the editor's rules where they miss the output sample, rather than write new ones.</summary>
    public bool Refine { get; set; }

    /// <summary>
    /// Notes for the model from the person building the mapping, for what the samples don't show -
    /// codes a lookup needs that the sample doesn't happen to contain, say.
    /// </summary>
    public string? Instructions { get; set; }
}

public class MappingSuggestionResponse
{
    /// <summary>The rules, serialised the way the editor stores them.</summary>
    public string MappingRules { get; set; } = "";

    /// <summary>The source sample mapped with these rules - a preview, run before answering.</summary>
    public string? OutputDocument { get; set; }

    public string? ContentType { get; set; }

    public List<MappingPreviewError> RuleErrors { get; set; } = new();

    /// <summary>Set when the rules could not be run against the sample at all.</summary>
    public string? Error { get; set; }

    /// <summary>Where the output differs from the output sample, at most <see cref="DocumentDifferences.Limit"/>.</summary>
    public List<MappingDifference> Differences { get; set; } = new();

    /// <summary>Whether the rules turn the source sample into the output sample exactly.</summary>
    public bool MatchesTarget { get; set; }

    /// <summary>The model that wrote the rules, so the editor can say which.</summary>
    public string? Model { get; set; }
}

/// <summary>
/// Asks the model Settings, AI names to write mapping rules from a source sample and an output
/// sample, and checks them before answering.
/// </summary>
/// <remarks>
/// <para>
/// The check is a preview - the same read, map and write the pipeline runs - and a comparison with
/// the output sample, so the editor can say whether the rules do what was asked instead of the
/// person having to find out.
/// </para>
/// <para>
/// One model call per request, and a second only when the first answer isn't readable rules at all.
/// Fixing differences is the editor's next request, with <see cref="MappingSuggestionRequest.Refine"/>:
/// a model thinking about a large document can take most of a minute, and two of those in one
/// request would outlast the proxies in front of many deployments.
/// </para>
/// </remarks>
public class Suggest(RequestContext requestContext, BitweenDbContext dbContext,
        MappingContextFactory contextFactory, AiModels models)
    : ICommandHandler<MappingSuggestionRequest, MappingSuggestionResponse>
{
    /// <summary>
    /// Four times the 300-line sample generation was tested with, which answered in two seconds:
    /// room for a real document, without a pasted export becoming a very large and slow prompt.
    /// </summary>
    internal const int MaxSampleLength = 200_000;

    internal const int MaxInstructionsLength = 4_000;

    public async Task<MappingSuggestionResponse> Handle(MappingSuggestionRequest request)
    {
        // The same right as the preview it runs: the editor opens while creating and while editing.
        await requestContext.EnsurePermission(dbContext,
            Model.Permissions.Subscriptions.Create, Model.Permissions.Subscriptions.Edit);

        // Before anything else costs time or tokens: whatever is sent here goes to a paid provider.
        if (request.SourceDocument?.Length > MaxSampleLength || request.TargetDocument?.Length > MaxSampleLength)
            throw new SWValidationException("AI_SAMPLES",
                $"Samples are limited to {MaxSampleLength / 1000:N0} KB each. A few records are enough to learn a mapping from.");
        if (request.Instructions?.Length > MaxInstructionsLength)
            throw new SWValidationException("AI_SAMPLES", $"Instructions are limited to {MaxInstructionsLength:N0} characters.");

        var model = models.Current();

        MappingRules editor;
        try
        {
            editor = JsonConvert.DeserializeObject<MappingRules>(request.MappingRules ?? "") ?? new MappingRules();
        }
        catch (JsonException ex)
        {
            throw new SWValidationException("MAPPING_RULES", $"The mapping rules could not be read: {ex.Message}");
        }
        if (MappingRulesPrompt.NullIn(editor) is { } missing)
            throw new SWValidationException("MAPPING_RULES", $"The mapping rules could not be read: {missing}");

        if (string.IsNullOrWhiteSpace(request.SourceDocument))
            throw new SWValidationException("AI_SAMPLES", "Paste a sample of the source document first.");
        if (string.IsNullOrWhiteSpace(request.TargetDocument))
            throw new SWValidationException("AI_SAMPLES", "Paste a sample of the output document first.");

        // Both samples are read before the model is asked anything: a sample that isn't valid in
        // its format would only come back as a failed check, after paying for a call.
        var source = Preview.SourceFormat(editor, request.SourceDocument)
                     ?? throw new SWValidationException("MAPPING_RULES", DocumentFormats.Unsupported(editor.SourceFormat, "source"));
        var target = Preview.TargetFormat(editor)
                     ?? throw new SWValidationException("MAPPING_RULES", DocumentFormats.Unsupported(editor.TargetFormat, "target"));
        var input = Read(source, request.SourceDocument, "source");
        var expected = Read(target, request.TargetDocument, "output");

        (MappingRules, IReadOnlyList<string>)? previous = null;
        if (request.Refine)
        {
            var tried = await Check(editor, request, input, target, expected);
            if (tried.MatchesTarget) return tried;
            previous = (editor, Problems(tried));
        }

        // The same masked context the preview maps against, so a name offered here is one the
        // check can resolve. Only its names leave the server.
        var names = MappingRulesPrompt.NamesOf(await contextFactory.Build(request.PartnerId, maskSecrets: true));
        var message = MappingRulesPrompt.Message(editor, request.SourceDocument, request.TargetDocument,
            names, request.Instructions, previous);
        MappingRules rules;
        try
        {
            rules = MappingRulesPrompt.Parse(await Ask(model, message), editor);
        }
        catch (FormatException first)
        {
            // Once more, told why: what went wrong is something a model can act on.
            var again = $"{message}\nYour previous answer could not be used: {first.Message}\n" +
                        "Answer again with only the rules JSON.";
            try
            {
                rules = MappingRulesPrompt.Parse(await Ask(model, again), editor);
            }
            catch (FormatException second)
            {
                throw new SWValidationException("AI_UNUSABLE_ANSWER",
                    $"The model's answer wasn't usable mapping rules ({second.Message}). Try again.");
            }
        }

        var checkedRules = await Check(rules, request, input, target, expected);
        checkedRules.Model = models.ModelName;
        return checkedRules;
    }

    static Task<string> Ask(IAiModel model, string message) =>
        model.Complete(new AiPrompt(MappingRulesPrompt.Instructions, message, Json: true));

    static ValueNode Read(IDocumentFormat format, string sample, string which)
    {
        try
        {
            return format.Read(sample);
        }
        catch (DocumentFormatException ex)
        {
            throw new SWValidationException("AI_SAMPLES", $"The {which} sample isn't valid {format.Id}: {ex.Message}");
        }
    }

    /// <summary>Previews the rules against the source sample and compares the result with the output sample.</summary>
    async Task<MappingSuggestionResponse> Check(MappingRules rules, MappingSuggestionRequest request,
        ValueNode input, IDocumentFormat target, ValueNode expected)
    {
        var serialized = MappingRulesPrompt.Serialize(rules);
        var preview = await new Preview(requestContext, dbContext, contextFactory).Handle(new MappingPreviewRequest
        {
            MappingRules = serialized,
            SourceDocument = request.SourceDocument,
            PartnerId = request.PartnerId,
        });

        var response = new MappingSuggestionResponse
        {
            MappingRules = serialized,
            OutputDocument = preview.OutputDocument,
            ContentType = preview.ContentType,
            RuleErrors = preview.RuleErrors,
            Error = preview.Error,
        };

        // Read back with the format that wrote it, so the comparison is between two trees rather
        // than two texts that differ only in whitespace or key order.
        if (preview.OutputDocument is not null)
        {
            try
            {
                response.Differences = DocumentDifferences.Between(expected, target.Read(preview.OutputDocument));
            }
            catch (DocumentFormatException ex)
            {
                response.Differences.Add(new("(root)", $"the output could not be read back as {target.Id}: {ex.Message}"));
            }
        }

        // A value frozen from the source matches this sample and no other, which the comparison
        // can't see: it is a difference all the same.
        response.Differences.AddRange(FrozenValues.In(rules, input));

        response.MatchesTarget = preview.OutputDocument is not null && preview.Error is null
                                 && preview.RuleErrors.Count == 0 && response.Differences.Count == 0;
        return response;
    }

    static List<string> Problems(MappingSuggestionResponse tried) =>
    [
        .. tried.Error is null ? [] : new[] { tried.Error },
        .. tried.RuleErrors.Select(e => $"The rule for {e.Target} failed: {e.Reason}"),
        .. tried.Differences.Select(d => d.ToString()),
    ];
}
