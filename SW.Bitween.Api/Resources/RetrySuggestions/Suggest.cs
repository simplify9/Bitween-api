#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.Bitween.Services.Ai;
using SW.Bitween.Services.Metrics;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.RetrySuggestions;

public class RetrySuggestionRequest
{
    public int SubscriptionId { get; set; }

    /// <summary>How far back to look, 1 to 90 days.</summary>
    public int Days { get; set; } = 30;
}

/// <summary>One kind of failure, and what the current and the suggested policy each do with it.</summary>
public class RetrySuggestionCoverage
{
    /// <summary>The error's first line, masked as it was sent.</summary>
    public string Error { get; set; } = "";
    public int Count { get; set; }
    public int Retried { get; set; }
    public int FixedByRetry { get; set; }
    public string Now { get; set; } = "";
    public string Suggested { get; set; } = "";
}

public class RetrySuggestionResponse
{
    public string Name { get; set; } = "";
    public string Summary { get; set; } = "";

    /// <summary>The groups, ready for <c>POST /api/retrypolicies</c>; nothing is saved until then.</summary>
    public List<RetryGroup> Groups { get; set; } = new();

    public List<RetrySuggestionCoverage> Coverage { get; set; } = new();
    public int Failures { get; set; }
    public int Days { get; set; }
    public string? Model { get; set; }

    /// <summary>Exactly what went to the provider.</summary>
    public string Sent { get; set; } = "";
}

/// <summary>
/// Proposes a retry policy for a subscription from its own recent failures.
/// </summary>
/// <remarks>
/// <para>
/// The provider sees each kind of failure as a masked line with counts and how often a retry fixed
/// it, and the current policy's rules. Never a document or a response body. The suggestion is then
/// checked as a saved policy would be and run against the real failures here, so the coverage
/// shows what changes for each before anyone saves it.
/// </para>
/// <para>
/// It saves nothing: saving the suggestion is creating a policy, so asking needs that right too,
/// and a question nobody could act on isn't sent. Each one is recorded as an operator action,
/// since it sends something about the subscription to a third party.
/// </para>
/// </remarks>
public class Suggest(RequestContext requestContext, BitweenDbContext dbContext, AiModels models)
    : ICommandHandler<RetrySuggestionRequest, RetrySuggestionResponse>
{
    public async Task<RetrySuggestionResponse> Handle(RetrySuggestionRequest request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Subscriptions.View);
        await requestContext.EnsurePermission(dbContext, Model.Permissions.RetryPolicies.Create);
        var model = models.Current();

        var subscription = await dbContext.Set<Subscription>().AsNoTracking()
                               .Include(s => s.RetryPolicy)
                               .FirstOrDefaultAsync(s => s.Id == request.SubscriptionId)
                           ?? throw new SWNotFoundException(request.SubscriptionId.ToString());
        var current = subscription.CustomRetryPolicy?.Groups ?? subscription.RetryPolicy?.Groups;
        var days = Math.Clamp(request.Days, 1, 90);
        var since = DateTime.UtcNow.AddDays(-days);

        // The newest failures, by the (SubscriptionId, FinishedOn) index; only the start of each
        // error, which is where its type and message are.
        var failed = await dbContext.Set<XchangeResult>().AsNoTracking()
            .Where(r => r.SubscriptionId == subscription.Id && r.FinishedOn >= since && (!r.Success || r.ResponseBad))
            .OrderByDescending(r => r.FinishedOn)
            .Take(RetryPolicyPrompt.MaxFailures)
            .Select(r => new
            {
                r.Id, r.Success, r.FinishedOn,
                Exception = r.Exception == null ? null : r.Exception.Substring(0, RetryPolicyPrompt.SampleLength),
            })
            .ToListAsync();
        var errors = failed.Where(f => !f.Success).ToList();
        if (errors.Count == 0)
            throw new SWValidationException("NO_FAILURES",
                $"This subscription has no failed exchanges in the last {days} days, so there is nothing to learn a policy from.");

        // Whether each one's retry succeeded: the attempt made from it, by the RetryFor index.
        var ids = errors.Select(e => e.Id).ToList();
        var retries = await dbContext.Set<Xchange>().AsNoTracking()
            .Where(x => x.RetryFor != null && ids.Contains(x.RetryFor))
            .Join(dbContext.Set<XchangeResult>(), x => x.Id, r => r.Id, (x, r) => new { x.RetryFor, Fixed = r.Success && !r.ResponseBad })
            .ToListAsync();
        var retriedFrom = retries.GroupBy(r => r.RetryFor!).ToDictionary(g => g.Key, g => g.Any(r => r.Fixed));

        var kinds = errors
            .GroupBy(e => ErrorFingerprint.Of(e.Exception ?? ""))
            .Select(g =>
            {
                var newest = g.First();
                var sample = newest.Exception ?? "";
                return new FailureKind(sample, RetryPolicyPrompt.Mask(ErrorFingerprint.Sample(sample)), g.Count(),
                    g.Count(e => retriedFrom.ContainsKey(e.Id)), g.Count(e => retriedFrom.TryGetValue(e.Id, out var ok) && ok),
                    g.Min(e => e.FinishedOn), g.Max(e => e.FinishedOn));
            })
            .OrderByDescending(k => k.Count)
            .Take(RetryPolicyPrompt.MaxKinds)
            .ToList();

        var describe = $"Subscription kind: {subscription.Type}; mapper: {subscription.MapperId ?? "none"}; handler: {subscription.HandlerId ?? "none"}";
        var message = RetryPolicyPrompt.Message(describe, days, errors.Count, failed.Count - errors.Count, kinds, current);
        // Recorded before it goes, so a call that fails or times out after sending is on record too.
        await dbContext.RecordNowAsync(requestContext, OperatorAction.SuggestRetryPolicy, $"subscription {subscription.Id}",
            $"Sent to {models.ModelName}: {errors.Count} failures, errors masked.");
        var suggestion = RetryPolicyPrompt.Parse(
            await model.Complete(new AiPrompt(RetryPolicyPrompt.Instructions, message, Json: true, MaxTokens: 4000)));

        var coverage = new List<RetrySuggestionCoverage>();
        foreach (var kind in kinds)
            coverage.Add(new RetrySuggestionCoverage
            {
                Error = kind.Masked,
                Count = kind.Count,
                Retried = kind.Retried,
                FixedByRetry = kind.FixedByRetry,
                Now = await RetryPolicyPrompt.Outcome(current, kind.Sample),
                Suggested = await RetryPolicyPrompt.Outcome(suggestion.Groups, kind.Sample),
            });

        return new RetrySuggestionResponse
        {
            Name = suggestion.Name,
            Summary = suggestion.Summary,
            Groups = suggestion.Groups,
            Coverage = coverage,
            Failures = errors.Count,
            Days = days,
            Model = models.ModelName,
            Sent = message,
        };
    }
}
