using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.Domain;
using SW.Bitween.Model;

namespace SW.Bitween.UnitTests;

[TestClass]
public class RetryAlertResolverTests
{
    // ─── Helpers ────────────────────────────────────────────────────────────────

    private const int PolicyChannel = 1, GroupChannel = 2, OverrideChannel = 3;

    private static RetryGroup Group(RetryAlertMode mode = RetryAlertMode.Inherit, int? channel = null) =>
        new()
        {
            Name = "timeouts",
            AppliesTo = [XchangeResultType.Error],
            AlertMode = mode,
            AlertChannelId = channel
        };

    private static RetryPolicy Policy(int? channel = null) => new()
    {
        Name = "policy",
        AlertChannelId = channel
    };

    private static RetryAlertOverride Override(RetryAlertMode mode, int? channel = null) => new()
    {
        SubscriptionId = 1,
        AlertMode = mode,
        AlertChannelId = channel
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
        var target = RetryAlertResolver.Resolve(null, Group(), Policy(PolicyChannel));

        Assert.IsNotNull(target);
        Assert.AreEqual(PolicyChannel, target.ChannelId);
        Assert.AreEqual(RetryAlertLevel.Policy, target.Level);
    }

    // ─── Group level ────────────────────────────────────────────────────────────

    [TestMethod]
    public void GroupSend_ReplacesPolicyEntirely()
    {
        var target = RetryAlertResolver.Resolve(null,
            Group(RetryAlertMode.Send, GroupChannel), Policy(PolicyChannel));

        Assert.AreEqual(GroupChannel, target.ChannelId);
        Assert.AreEqual(RetryAlertLevel.Group, target.Level);
    }

    [TestMethod]
    public void GroupSilent_SuppressesPolicyAlert()
    {
        Assert.IsNull(RetryAlertResolver.Resolve(null,
            Group(RetryAlertMode.Silent), Policy(PolicyChannel)));
    }

    [TestMethod]
    public void GroupInherit_FallsThroughToPolicy()
    {
        var target = RetryAlertResolver.Resolve(null,
            Group(RetryAlertMode.Inherit), Policy(PolicyChannel));

        Assert.AreEqual(RetryAlertLevel.Policy, target.Level);
    }

    // ─── Subscription + group level ─────────────────────────────────────────────

    [TestMethod]
    public void SubscriptionOverrideSend_WinsOverGroupAndPolicy()
    {
        var target = RetryAlertResolver.Resolve(
            Override(RetryAlertMode.Send, OverrideChannel),
            Group(RetryAlertMode.Send, GroupChannel),
            Policy(PolicyChannel));

        Assert.AreEqual(OverrideChannel, target.ChannelId);
        Assert.AreEqual(RetryAlertLevel.SubscriptionGroup, target.Level);
    }

    [TestMethod]
    public void SubscriptionOverrideSilent_SuppressesEverythingAbove()
    {
        Assert.IsNull(RetryAlertResolver.Resolve(
            Override(RetryAlertMode.Silent),
            Group(RetryAlertMode.Send, GroupChannel),
            Policy(PolicyChannel)));
    }

    [TestMethod]
    public void SubscriptionOverrideInherit_FallsThroughToGroup()
    {
        var target = RetryAlertResolver.Resolve(
            Override(RetryAlertMode.Inherit),
            Group(RetryAlertMode.Send, GroupChannel),
            Policy(PolicyChannel));

        Assert.AreEqual(RetryAlertLevel.Group, target.Level);
    }

    // ─── Edge cases ─────────────────────────────────────────────────────────────

    [TestMethod]
    public void InlineCustomPolicy_HasNoPolicyLevel_ButGroupStillSends()
    {
        // A subscription with a CustomRetryPolicy has no policy row at all.
        var target = RetryAlertResolver.Resolve(null, Group(RetryAlertMode.Send, GroupChannel), null);

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
        var target = RetryAlertResolver.Resolve(null, null, Policy(PolicyChannel));

        Assert.AreEqual(RetryAlertLevel.Policy, target.Level);
    }

    [TestMethod]
    public void SendWithNoChannel_FallsThroughRatherThanSilencing()
    {
        // Validation rejects this on save, so it only exists on rows written before that guard.
        // Falling through is more useful than silently sending nothing.
        var target = RetryAlertResolver.Resolve(
            Override(RetryAlertMode.Send),
            Group(RetryAlertMode.Send),
            Policy(PolicyChannel));

        Assert.AreEqual(RetryAlertLevel.Policy, target.Level);
    }
}
