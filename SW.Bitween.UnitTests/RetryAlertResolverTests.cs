using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.Domain;
using SW.Bitween.Model;

namespace SW.Bitween.UnitTests;

[TestClass]
public class RetryAlertResolverTests
{
    // ─── Helpers ────────────────────────────────────────────────────────────────

    private static RetryGroup Group(RetryAlertMode mode = RetryAlertMode.Inherit, string handler = null) =>
        new()
        {
            Name = "timeouts",
            AppliesTo = [XchangeResultType.Error],
            AlertMode = mode,
            AlertHandlerId = handler,
            AlertHandlerProperties = handler == null ? null : new Dictionary<string, string> { ["to"] = "group@x" }
        };

    private static RetryPolicy Policy(string handler = null) => new()
    {
        Name = "policy",
        AlertHandlerId = handler,
        AlertHandlerProperties = handler == null ? null : new Dictionary<string, string> { ["to"] = "policy@x" }
    };

    private static RetryAlertOverride Override(RetryAlertMode mode, string handler = null) => new()
    {
        SubscriptionId = 1,
        AlertMode = mode,
        AlertHandlerId = handler,
        AlertHandlerProperties = handler == null ? null : new Dictionary<string, string> { ["to"] = "sub@x" }
    };

    // ─── Nothing configured ─────────────────────────────────────────────────────

    [TestMethod]
    public void NoLevelConfigured_ResolvesToNothing()
    {
        Assert.IsNull(RetryAlertResolver.Resolve(null, Group(), Policy()));
    }

    // ─── Policy level ───────────────────────────────────────────────────────────

    [TestMethod]
    public void PolicyOnly_ResolvesToPolicy()
    {
        var target = RetryAlertResolver.Resolve(null, Group(), Policy("native.smtp"));

        Assert.IsNotNull(target);
        Assert.AreEqual("native.smtp", target.HandlerId);
        Assert.AreEqual(RetryAlertLevel.Policy, target.Level);
        Assert.AreEqual("policy@x", target.HandlerProperties["to"]);
    }

    // ─── Group level ────────────────────────────────────────────────────────────

    [TestMethod]
    public void GroupSend_ReplacesPolicyEntirely()
    {
        var target = RetryAlertResolver.Resolve(null,
            Group(RetryAlertMode.Send, "native.teams"), Policy("native.smtp"));

        Assert.AreEqual("native.teams", target.HandlerId);
        Assert.AreEqual(RetryAlertLevel.Group, target.Level);
        // Replace, not merge: nothing of the policy's own properties survives.
        Assert.AreEqual("group@x", target.HandlerProperties["to"]);
    }

    [TestMethod]
    public void GroupSilent_SuppressesPolicyAlert()
    {
        Assert.IsNull(RetryAlertResolver.Resolve(null,
            Group(RetryAlertMode.Silent), Policy("native.smtp")));
    }

    [TestMethod]
    public void GroupInherit_FallsThroughToPolicy()
    {
        var target = RetryAlertResolver.Resolve(null,
            Group(RetryAlertMode.Inherit), Policy("native.smtp"));

        Assert.AreEqual(RetryAlertLevel.Policy, target.Level);
    }

    // ─── Subscription + group level ─────────────────────────────────────────────

    [TestMethod]
    public void SubscriptionOverrideSend_WinsOverGroupAndPolicy()
    {
        var target = RetryAlertResolver.Resolve(
            Override(RetryAlertMode.Send, "native.webhook"),
            Group(RetryAlertMode.Send, "native.teams"),
            Policy("native.smtp"));

        Assert.AreEqual("native.webhook", target.HandlerId);
        Assert.AreEqual(RetryAlertLevel.SubscriptionGroup, target.Level);
        Assert.AreEqual("sub@x", target.HandlerProperties["to"]);
    }

    [TestMethod]
    public void SubscriptionOverrideSilent_SuppressesEverythingAbove()
    {
        Assert.IsNull(RetryAlertResolver.Resolve(
            Override(RetryAlertMode.Silent),
            Group(RetryAlertMode.Send, "native.teams"),
            Policy("native.smtp")));
    }

    [TestMethod]
    public void SubscriptionOverrideInherit_FallsThroughToGroup()
    {
        var target = RetryAlertResolver.Resolve(
            Override(RetryAlertMode.Inherit),
            Group(RetryAlertMode.Send, "native.teams"),
            Policy("native.smtp"));

        Assert.AreEqual(RetryAlertLevel.Group, target.Level);
    }

    // ─── Edge cases ─────────────────────────────────────────────────────────────

    [TestMethod]
    public void InlineCustomPolicy_HasNoPolicyLevel_ButGroupStillSends()
    {
        // A subscription with a CustomRetryPolicy has no policy row at all.
        var target = RetryAlertResolver.Resolve(null, Group(RetryAlertMode.Send, "native.teams"), null);

        Assert.AreEqual(RetryAlertLevel.Group, target.Level);
    }

    [TestMethod]
    public void InlineCustomPolicy_WithInheritingGroup_ResolvesToNothing()
    {
        Assert.IsNull(RetryAlertResolver.Resolve(null, Group(), null));
    }

    [TestMethod]
    public void MissingGroup_StillFallsBackToPolicy()
    {
        // The group was removed from the policy between the failure and the send.
        var target = RetryAlertResolver.Resolve(null, null, Policy("native.smtp"));

        Assert.AreEqual(RetryAlertLevel.Policy, target.Level);
    }

    [TestMethod]
    public void SendWithNoHandler_FallsThroughRatherThanSilencing()
    {
        // Validation rejects this on save, so it only exists on rows written before that guard.
        // Falling through is more useful than silently sending nothing.
        var target = RetryAlertResolver.Resolve(
            Override(RetryAlertMode.Send),
            Group(RetryAlertMode.Send),
            Policy("native.smtp"));

        Assert.AreEqual(RetryAlertLevel.Policy, target.Level);
    }
}
