#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Services.Ai;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.XchangeQueries;

public class XchangeQueryRequest
{
    public string Question { get; set; } = "";

    /// <summary>The asker's time zone, as the browser names it: "Asia/Amman". UTC when missing or unknown.</summary>
    public string? TimeZone { get; set; }
}

public class XchangeQueryResponse
{
    /// <summary>The Exchanges page's URL parameters: <c>status=failed&amp;partnerId=12&amp;range=7d</c>.</summary>
    public string Query { get; set; } = "";

    /// <summary>A sentence for above the result, with <c>{count}</c> where the page's total goes.</summary>
    public string Answer { get; set; } = "";

    /// <summary>What the filter couldn't say.</summary>
    public List<string> Notes { get; set; } = new();

    public string? Model { get; set; }

    /// <summary>Exactly what went to the provider.</summary>
    public string Sent { get; set; } = "";
}

/// <summary>
/// Turns a plain-English question about exchanges into the Exchanges page's filter.
/// </summary>
/// <remarks>
/// The model is sent the question, the time and names - of partners, subscriptions, information
/// types and promoted-property keys - and nothing about any exchange. The filter it answers with
/// is the page's own, so it runs as every other filter does, with the asker's rights.
/// Each question is recorded as an operator action, since it goes to a third party.
/// </remarks>
public class Ask(RequestContext requestContext, BitweenDbContext dbContext, AiModels models)
    : ICommandHandler<XchangeQueryRequest, XchangeQueryResponse>
{
    public async Task<XchangeQueryResponse> Handle(XchangeQueryRequest request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Exchanges.View);

        var question = request.Question?.Trim() ?? "";
        if (question.Length == 0)
            throw new SWValidationException("QUESTION", "Type a question about your exchanges first.");
        if (question.Length > ExchangeQueryPrompt.MaxQuestionLength)
            throw new SWValidationException("QUESTION",
                $"Questions are limited to {ExchangeQueryPrompt.MaxQuestionLength} characters.");

        var model = models.Current();

        var zone = TimeZoneOf(request.TimeZone);
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone);
        var names = await NamesAsync();
        var message = ExchangeQueryPrompt.Message(question, now, zone.Id, names);

        // Recorded before it goes, so a call that fails or times out after sending is on record too.
        await dbContext.RecordNowAsync(requestContext, OperatorAction.AskExchanges, "exchanges",
            $"Asked {models.ModelName}: {question}");
        var answer = await model.Complete(new AiPrompt(ExchangeQueryPrompt.Instructions, message, Json: true, MaxTokens: 2000));
        var query = ExchangeQueryPrompt.Parse(answer, names);

        return new XchangeQueryResponse
        {
            Query = query.Query,
            Answer = query.Answer,
            Notes = query.Notes.ToList(),
            Model = models.ModelName,
            Sent = message,
        };
    }

    async Task<QueryNames> NamesAsync()
    {
        var partners = await dbContext.Set<Partner>().AsNoTracking()
            .OrderBy(p => p.Name).Select(p => new QueryName(p.Id, p.Name, null))
            .Take(ExchangeQueryPrompt.MaxNames + 1).ToListAsync();

        var subscriptions = (await dbContext.Set<Subscription>().AsNoTracking()
                .OrderBy(s => s.Name)
                .Select(s => new { s.Id, s.Name, s.Type, s.PartnerId, s.DocumentId, s.Inactive })
                .Take(ExchangeQueryPrompt.MaxNames + 1).ToListAsync())
            .Select(s => new QueryName(s.Id, s.Name,
                $"{s.Type}, partner {(s.PartnerId?.ToString() ?? "none")}, information type {s.DocumentId}{(s.Inactive ? ", inactive" : "")}"))
            .ToList();

        var documents = await dbContext.Set<Document>().AsNoTracking()
            .OrderBy(d => d.Name).Take(ExchangeQueryPrompt.MaxNames + 1)
            .Select(d => new { d.Id, d.Name, d.PromotedProperties })
            .ToListAsync();
        var informationTypes = documents
            .Select(d => new QueryName(d.Id, d.Name, d.PromotedProperties is { Count: > 0 } promoted
                ? "promotes " + string.Join(", ", promoted.Keys)
                : null))
            .ToList();
        var keys = documents.SelectMany(d => d.PromotedProperties?.Keys ?? Enumerable.Empty<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();

        return new QueryNames(partners, subscriptions, informationTypes, keys);
    }

    static TimeZoneInfo TimeZoneOf(string? id) =>
        !string.IsNullOrWhiteSpace(id) && TimeZoneInfo.TryFindSystemTimeZoneById(id.Trim(), out var zone)
            ? zone
            : TimeZoneInfo.Utc;
}
