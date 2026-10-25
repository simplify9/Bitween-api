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
/// <param name="NeedsYou">What only a person can do first.</param>
public record MirrorSubscriptionView(string Key, bool OnProd, string ProdId, string ProdUrl, IReadOnlyList<MirrorDiff> Used,
    IReadOnlyList<MirrorPiece> ToBring, IReadOnlyList<MirrorNote> NeedsYou);

/// <summary>A run of a subscription's test, by the correlation its exchanges carry, and when it started.</summary>
public record MirrorTestRun(string Correlation, DateTimeOffset StartedOn);

/// <summary>
/// A subscription's test with prod's traffic: prod's latest arrivals it takes part in, faked, each sent in
/// on staging the way it came in on prod, and how they last ran. What the subscription's page shows, with
/// how work under way is going.
/// </summary>
/// <param name="State">idle; taking, while prod's arrivals are being taken; running; or failed, and <paramref name="Error"/> says why.</param>
/// <param name="Busy">Other work of the mirror under way, which this test waits for, in words: The test of “Push labels”; null when there is none.</param>
/// <param name="TakenOn">When prod's arrivals were taken; null before they are.</param>
/// <param name="Reason">Why prod has none to take, when it has none.</param>
/// <param name="RanOn">When they last ran on staging; null before they do, or once cleared.</param>
/// <param name="Runs">How each arrival ran, by its id.</param>
/// <param name="SetupError">Why what each exchange used wasn't compared with prod's setup, when it wasn't.</param>
/// <param name="Made">Each run whose exchanges are still on staging, for clearing them.</param>
/// <param name="Exchanges">How many exchanges the test's runs have left on staging.</param>
public record MirrorTest(string State, string Busy, int Done, int Total, string Error, DateTimeOffset? TakenOn, string Reason,
    IReadOnlyList<MirrorFamily> Families, DateTimeOffset? RanOn, IReadOnlyList<MirrorFamilyRun> Runs, string SetupError,
    IReadOnlyList<MirrorTestRun> Made, int Exchanges);
