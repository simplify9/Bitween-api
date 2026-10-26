using System;
using System.Collections.Generic;

namespace SW.Bitween.Services.Mirror;

/// <summary>One line of a rehearsed exchange's compare: something of prod's beside staging's.</summary>
/// <param name="Part">
/// result, output, answer, calls, input or used: the part of the exchange it is about; entry or family for
/// a family's: how it came in, and which exchanges it led to.
/// </param>
/// <param name="Name">What is compared, in words: Result, What it sent, Handler, Partner “Acme”.</param>
/// <param name="Level">
/// different or check (see <see cref="MirrorCompare"/>); info, a difference that explains rather than
/// counts, such as the upgrade itself; faked, the same as prod's but with faked values; or same.
/// </param>
/// <param name="Prod">How it is on prod, as staging can show it: faked, like everything of prod's.</param>
/// <param name="Staging">How it is on staging.</param>
/// <param name="Note">What explains it, when something does.</param>
/// <param name="Fields">Field by field, where there is more than one thing to show.</param>
/// <param name="Section">For a setup item: its kind, so the page can open it.</param>
/// <param name="StagingId">For a setup item: its id on staging.</param>
/// <param name="Type">For a subscription: its type, which decides its page.</param>
public record MirrorDiff(string Part, string Name, string Level, string Prod, string Staging, string Note = null,
    IReadOnlyList<MirrorFieldDiff> Fields = null, string Section = null, string StagingId = null, string Type = null);

/// <summary>One field of a compared file or setup item.</summary>
/// <param name="Path">Where it is: order.lines[2].sku, Order/@id, row 3 · column 2, handler · properties · Url.</param>
public record MirrorFieldDiff(string Path, string Level, string Prod, string Staging);

/// <summary>One exchange of a rehearsed family: prod's, the twin staging ran, and how the two compare.</summary>
/// <param name="ProdExchangeId">Prod's exchange; null for one staging ran that prod's family didn't.</param>
/// <param name="Subscription">Its subscription, by name, or the information type of a document being routed.</param>
/// <param name="StagingExchangeId">The twin staging ran; null when it didn't run one.</param>
/// <param name="Outcome">How it ended on staging: success, badResponse or failed; null when it didn't run or finish.</param>
/// <param name="Status">The HTTP status staging's error names, where it names one.</param>
/// <param name="Verdict">same, check or different; null when it didn't run or finish.</param>
/// <param name="NotRun">Why it didn't run, or didn't finish, in words for the page.</param>
/// <param name="Output">What staging's exchange sent.</param>
/// <param name="Response">What staging's exchange got back.</param>
public record MirrorRun(string ProdExchangeId, string Subscription, string StagingExchangeId, DateTimeOffset RanOn, string Outcome,
    int? Status, IReadOnlyList<string> ErrorTypes, string Verdict, string NotRun, MirrorSampleFile Output, MirrorSampleFile Response,
    IReadOnlyList<MirrorDiff> Diffs);

/// <summary>
/// One of prod's families, sent in on staging the way it came in on prod - through the gateway as the
/// partner, as a document routed, as a file received, as an aggregation - and every exchange staging
/// ran for it, each compared with its twin on prod.
/// </summary>
/// <param name="FamilyId">The family: its first exchange's id on prod.</param>
/// <param name="Correlation">The correlation id the family ran under on staging; null when it didn't run.</param>
/// <param name="Verdict">same, check or different: the worst of its exchanges and how it came in; null when it didn't run.</param>
/// <param name="NotRun">Why it didn't run, in words for the page.</param>
/// <param name="Diffs">The family's own lines: how it came in, and exchanges one side ran and the other didn't.</param>
public record MirrorFamilyRun(string FamilyId, string Correlation, DateTimeOffset RanOn, string Verdict, string NotRun,
    IReadOnlyList<MirrorDiff> Diffs, IReadOnlyList<MirrorRun> Members);
