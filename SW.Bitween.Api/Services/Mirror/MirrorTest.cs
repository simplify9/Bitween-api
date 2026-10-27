using System;
using System.Collections.Generic;

namespace SW.Bitween.Services.Mirror;

/// <summary>
/// One of staging's subscriptions beside prod's of the same name: how it and what it uses differ, and what
/// is left to do before it runs as on prod.
/// </summary>
/// <param name="OnProd">Whether prod has a subscription of this name; without one there is nothing to compare or test with.</param>
/// <param name="Used">
/// The subscription and each item it needs to run as on prod, beside prod's: the same, faked, different
/// field by field, or not on staging; then the adapters they run.
/// </param>
/// <param name="ToBring">What bringing it from prod would bring or can't bring yet, as <see cref="MirrorBring"/> says it.</param>
/// <param name="NeedsYou">What only a person can do first, the addresses and secrets to fill in among it, and what came faked.</param>
/// <param name="CustomAdapters">
/// The adapters it and what it needs run that don't come with Bitween: one may call addresses written in
/// its code, which staging can't see, so a test asks first.
/// </param>
public record MirrorSubscriptionView(string Key, bool OnProd, string ProdId, string ProdUrl, IReadOnlyList<MirrorDiff> Used,
    IReadOnlyList<MirrorPiece> ToBring, IReadOnlyList<MirrorNote> NeedsYou, IReadOnlyList<string> CustomAdapters = null);

/// <summary>A run of a subscription's test, by the correlation its exchanges carry, and when it started.</summary>
public record MirrorTestRun(string Correlation, DateTimeOffset StartedOn);

/// <summary>How a subscription's test last ran, kept after what it made is cleared: what the lists show.</summary>
/// <param name="Verdict">
/// same, check or different: the worst of its arrivals, one that didn't run counting as one to check; or
/// noTraffic, when prod had none of it to take.
/// </param>
public record MirrorTestVerdict(string Verdict, DateTimeOffset RanOn, int Same, int Check, int Different, int NotRun);

/// <summary>One of staging's items that prod has too, by name, and how its test last ran.</summary>
/// <param name="Id">Its id on staging.</param>
/// <param name="Type">A subscription's type: Receiving, Aggregation or Response, say.</param>
/// <param name="Verdict">
/// same, check or different; noTraffic when prod had none of it to take; null when it hasn't been tested
/// since prod's arrivals were taken. A gateway's is the worst of the tested subscriptions it leads to.
/// </param>
/// <param name="RanOn">When the test that says so ran; for a gateway, the latest.</param>
/// <param name="Tested">How many of the subscriptions it is, or a gateway leads to, have been tested, of <paramref name="Of"/>.</param>
/// <param name="Custom">It, or a subscription a gateway leads to, runs an adapter that doesn't come with Bitween.</param>
public record MirrorListedItem(int Id, string Key, string Type, string Verdict, DateTimeOffset? RanOn, int Tested, int Of, bool Custom);

/// <summary>
/// Staging's items of one kind that prod has too, each with how its test last ran: what the lists show and
/// filter by. With the test under way and those waiting their turn, and why prod can't be read, when it can't.
/// </summary>
public record MirrorListed(IReadOnlyList<MirrorListedItem> Items, string Testing, IReadOnlyList<string> Waiting, string Error = null);

/// <summary>
/// A subscription's test with prod's traffic: prod's latest arrivals it takes part in, faked, each sent in
/// on staging the way it came in on prod, and how they last ran. What the subscription's page shows, with
/// how work under way is going.
/// </summary>
/// <param name="State">
/// idle; taking, while prod's arrivals are being taken; running; queued, while it waits its turn after
/// <paramref name="Busy"/>; or failed, and <paramref name="Error"/> says why.
/// </param>
/// <param name="Busy">Other work of the mirror under way, which this test waits for, in words: The test of “Push labels”; null when there is none.</param>
/// <param name="TakenOn">When prod's arrivals were taken; null before they are.</param>
/// <param name="Reason">Why prod has none to take, when it has none.</param>
/// <param name="RanOn">When they last ran on staging; null before they do, or once cleared.</param>
/// <param name="Runs">How each arrival ran, by its id.</param>
/// <param name="SetupError">Why what each exchange used wasn't compared with prod's setup, when it wasn't.</param>
/// <param name="Made">Each run whose exchanges are still on staging, for clearing them.</param>
/// <param name="Exchanges">How many exchanges the test's runs have left on staging.</param>
/// <param name="Last">How the arrivals last ran, kept after a clear; null until they run.</param>
public record MirrorTest(string State, string Busy, int Done, int Total, string Error, DateTimeOffset? TakenOn, string Reason,
    IReadOnlyList<MirrorFamily> Families, DateTimeOffset? RanOn, IReadOnlyList<MirrorFamilyRun> Runs, string SetupError,
    IReadOnlyList<MirrorTestRun> Made, int Exchanges, MirrorTestVerdict Last = null);
