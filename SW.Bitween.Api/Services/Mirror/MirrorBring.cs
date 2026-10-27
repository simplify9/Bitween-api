using System;
using System.Collections.Generic;

namespace SW.Bitween.Services.Mirror;

/// <summary>How many of prod's items there are of each kind a page lists: where the pages offer to bring them.</summary>
/// <param name="Subscriptions">Prod's subscriptions by type, Receiving or Aggregation for instance, and all of them under "all".</param>
public record MirrorProdKinds(IReadOnlyDictionary<string, int> Subscriptions, int ApiGateways, int BusGateways, string Error = null);

/// <summary>An item's traffic on prod over the window: exchanges, how many failed, when the last one was, and each day's count.</summary>
public record MirrorProdCounts(long Total, long Failed, DateTimeOffset? LastOn, IReadOnlyList<long> Daily);

/// <summary>One of prod's items as a page offers it: its traffic, and whether staging has it.</summary>
/// <param name="Detail">What it is for, in a few words: a subscription's partner and information type, a gateway's partners.</param>
/// <param name="State">As in <see cref="MirrorItem"/>: inSync, differs, missing or waiting.</param>
public record MirrorProdItem(string Key, string ProdId, string Type, string Detail, string State, string StagingId, string Reason, MirrorProdCounts Traffic);

/// <summary>
/// Prod's items of one kind, busiest first. <paramref name="Days"/> is the window the traffic covers;
/// null with <paramref name="TrafficNote"/> when prod can't say, and the items are then by name.
/// </summary>
public record MirrorProdList(string Section, string Noun, string Plural, int? Days, string TrafficNote, IReadOnlyList<MirrorProdItem> Items);

/// <summary>
/// One item a bring touches, and what happens to it:
/// <list type="bullet">
/// <item>new: staging doesn't have it, and gets prod's, faked;</item>
/// <item>kept: staging has it, and keeps its own;</item>
/// <item>adds: staging has it, and gets only what leads to what is brought: a gateway's partner, a bus route;</item>
/// <item>waits: it can't come yet, and <paramref name="Reason"/> says why;</item>
/// <item>install: an adapter staging installs from the marketplace.</item>
/// </list>
/// </summary>
/// <param name="Why">Why it comes: picked, or what needs it.</param>
public record MirrorPiece(string Section, string Noun, string Key, string State, string Why, string Reason = null, string StagingId = null, string Type = null);

/// <summary>What bringing the picked items does, or did: every item it touches, what only a person can do, and what failed.</summary>
public record MirrorBring(bool Applied, IReadOnlyList<MirrorPiece> Pieces, IReadOnlyList<MirrorNote> NeedsYou, IReadOnlyList<MirrorFailure> Failed);
