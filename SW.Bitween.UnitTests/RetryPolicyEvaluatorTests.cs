using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.Model;

namespace SW.Bitween.UnitTests;

[TestClass]
public class RetryPolicyEvaluatorTests
{
    // ─── Helpers ────────────────────────────────────────────────────────────────

    private static IRetryPolicy PolicyWith(params RetryGroup[] groups) => new TestPolicy(groups);

    private static RetryGroup ErrorGroup(
        string name,
        Matcher matcher,
        int maxPerError = 5,
        int maxTotal = 100,
        DelayStrategy delay = null,
        int priority = 10,
        RetryAction action = RetryAction.Allow) =>
        new RetryGroup
        {
            Name = name,
            Priority = priority,
            Enabled = true,
            AppliesTo = [XchangeResultType.Error],
            Action = action,
            Matchers = [matcher],
            Budget = action == RetryAction.Allow ? new RetryBudget
            {
                MaxAttemptsPerError = maxPerError,
                MaxAttemptsTotal = maxTotal,
                DelayStrategy = delay ?? new FixedDelayStrategy { DelayMs = 1000 }
            } : null
        };

    private static RetryGroup BadResultGroup(
        string name,
        Matcher matcher,
        int maxPerError = 5,
        int maxTotal = 100,
        DelayStrategy delay = null) =>
        new RetryGroup
        {
            Name = name,
            Priority = 10,
            Enabled = true,
            AppliesTo = [XchangeResultType.BadResult],
            Matchers = [matcher],
            Budget = new RetryBudget
            {
                MaxAttemptsPerError = maxPerError,
                MaxAttemptsTotal = maxTotal,
                DelayStrategy = delay ?? new FixedDelayStrategy { DelayMs = 1000 }
            }
        };

    private sealed class TestPolicy(RetryGroup[] groups) : IRetryPolicy
    {
        public List<RetryGroup> Groups { get; } = new List<RetryGroup>(groups);
    }

    // ─── ContainsMatcher ────────────────────────────────────────────────────────

    [TestMethod]
    public void ContainsMatcher_MatchesSubstring()
    {
        var m = new ContainsMatcher { Value = "timeout" };
        Assert.IsTrue(m.IsMatch("Connection timeout occurred"));
    }

    [TestMethod]
    public void ContainsMatcher_NoMatch()
    {
        var m = new ContainsMatcher { Value = "timeout" };
        Assert.IsFalse(m.IsMatch("Something unrelated happened"));
    }

    [TestMethod]
    public void ContainsMatcher_CaseInsensitiveByDefault()
    {
        var m = new ContainsMatcher { Value = "TIMEOUT" };
        Assert.IsTrue(m.IsMatch("Connection timeout occurred"));
    }

    [TestMethod]
    public void ContainsMatcher_CaseSensitive_WrongCase_NoMatch()
    {
        var m = new ContainsMatcher { Value = "TIMEOUT", CaseSensitive = true };
        Assert.IsFalse(m.IsMatch("Connection timeout occurred"));
    }

    [TestMethod]
    public void ContainsMatcher_CaseSensitive_CorrectCase_Matches()
    {
        var m = new ContainsMatcher { Value = "timeout", CaseSensitive = true };
        Assert.IsTrue(m.IsMatch("Connection timeout occurred"));
    }

    // ─── RegexMatcher ───────────────────────────────────────────────────────────

    [TestMethod]
    public void RegexMatcher_PatternMatches()
    {
        var m = new RegexMatcher { Pattern = @"\d{3}" };
        Assert.IsTrue(m.IsMatch("Error code 404 returned"));
    }

    [TestMethod]
    public void RegexMatcher_PatternNoMatch()
    {
        var m = new RegexMatcher { Pattern = @"^fatal" };
        Assert.IsFalse(m.IsMatch("non-fatal error"));
    }

    [TestMethod]
    public void RegexMatcher_DefaultFlagCaseInsensitive()
    {
        var m = new RegexMatcher { Pattern = "timeout" }; // default flags = "i"
        Assert.IsTrue(m.IsMatch("TIMEOUT error"));
    }

    [TestMethod]
    public void RegexMatcher_ExplicitCaseSensitiveFlag_WrongCase_NoMatch()
    {
        var m = new RegexMatcher { Pattern = "timeout", Flags = "" };
        Assert.IsFalse(m.IsMatch("TIMEOUT error"));
    }

    // ─── ExceptionTypeMatcher ───────────────────────────────────────────────────

    [TestMethod]
    public void ExceptionTypeMatcher_MatchesExactType()
    {
        // Matcher compares the full qualified name extracted by the regex
        var m = new ExceptionTypeMatcher { Value = "System.TimeoutException" };
        Assert.IsTrue(m.IsMatch("System.TimeoutException: The operation timed out."));
    }

    [TestMethod]
    public void ExceptionTypeMatcher_NoMatch_UnrelatedContent()
    {
        var m = new ExceptionTypeMatcher { Value = "System.TimeoutException" };
        Assert.IsFalse(m.IsMatch("Something went wrong with the database."));
    }

    [TestMethod]
    public void ExceptionTypeMatcher_IncludeInner_MatchesInnerException()
    {
        // First match is System.Exception (outer); second is SqlException (inner)
        var m = new ExceptionTypeMatcher { Value = "SqlException", IncludeInner = true };
        var content = "System.Exception: outer ---> SqlException: inner details";
        Assert.IsTrue(m.IsMatch(content));
    }

    [TestMethod]
    public void ExceptionTypeMatcher_ExcludeInner_DoesNotMatchInnerException()
    {
        // IncludeInner = false → only the first match (System.Exception) is checked
        var m = new ExceptionTypeMatcher { Value = "SqlException", IncludeInner = false };
        var content = "System.Exception: outer ---> SqlException: inner details";
        Assert.IsFalse(m.IsMatch(content));
    }

    // ─── JsonPathMatcher ────────────────────────────────────────────────────────

    [TestMethod]
    public void JsonPathMatcher_Eq_Match()
    {
        var m = new JsonPathMatcher { Path = "$.error.code", Op = JsonPathOp.Eq, Value = "404" };
        Assert.IsTrue(m.IsMatch("{\"error\":{\"code\":\"404\"}}"));
    }

    [TestMethod]
    public void JsonPathMatcher_Eq_NoMatch()
    {
        var m = new JsonPathMatcher { Path = "$.error.code", Op = JsonPathOp.Eq, Value = "404" };
        Assert.IsFalse(m.IsMatch("{\"error\":{\"code\":\"500\"}}"));
    }

    [TestMethod]
    public void JsonPathMatcher_Contains_Match()
    {
        var m = new JsonPathMatcher { Path = "$.message", Op = JsonPathOp.Contains, Value = "not found" };
        Assert.IsTrue(m.IsMatch("{\"message\":\"Resource not found\"}"));
    }

    [TestMethod]
    public void JsonPathMatcher_Exists_PathPresent()
    {
        var m = new JsonPathMatcher { Path = "$.retryable", Op = JsonPathOp.Exists };
        Assert.IsTrue(m.IsMatch("{\"retryable\":true}"));
    }

    [TestMethod]
    public void JsonPathMatcher_Exists_PathAbsent()
    {
        var m = new JsonPathMatcher { Path = "$.retryable", Op = JsonPathOp.Exists };
        Assert.IsFalse(m.IsMatch("{\"other\":true}"));
    }

    [TestMethod]
    public void JsonPathMatcher_InvalidJson_ReturnsFalse()
    {
        var m = new JsonPathMatcher { Path = "$.error.code", Op = JsonPathOp.Eq, Value = "404" };
        Assert.IsFalse(m.IsMatch("not json at all"));
    }

    [TestMethod]
    public void JsonPathMatcher_ArrayIndexer_Match()
    {
        var m = new JsonPathMatcher { Path = "$.lines[0].status", Op = JsonPathOp.Eq, Value = "error" };
        Assert.IsTrue(m.IsMatch("{\"lines\":[{\"status\":\"error\"},{\"status\":\"ok\"}]}"));
    }

    // ─── Evaluator: basic routing ────────────────────────────────────────────────

    [TestMethod]
    public void Evaluator_MatchingGroup_AllowsRetry()
    {
        var policy = PolicyWith(ErrorGroup("transient", new ContainsMatcher { Value = "timeout" }));
        var ev = new RetryPolicyEvaluator(policy);
        var decision = ev.Evaluate(XchangeResultType.Error, "Connection timeout", 0);
        Assert.IsTrue(decision.ShouldRetry);
        Assert.AreEqual("transient", decision.MatchedGroupName);
    }

    [TestMethod]
    public void Evaluator_NoMatchingGroup_Blocks()
    {
        var policy = PolicyWith(ErrorGroup("transient", new ContainsMatcher { Value = "timeout" }));
        var ev = new RetryPolicyEvaluator(policy);
        var decision = ev.Evaluate(XchangeResultType.Error, "Disk full", 0);
        Assert.IsFalse(decision.ShouldRetry);
    }

    [TestMethod]
    public void Evaluator_WrongResultType_GroupSkipped()
    {
        var policy = PolicyWith(BadResultGroup("bad", new JsonPathMatcher { Path = "$.retryable", Op = JsonPathOp.Exists }));
        var ev = new RetryPolicyEvaluator(policy);
        // Group is for BadResult only — must be skipped for Error
        var decision = ev.Evaluate(XchangeResultType.Error, "some exception", 0);
        Assert.IsFalse(decision.ShouldRetry);
    }

    // ─── Evaluator: priority ordering ───────────────────────────────────────────

    [TestMethod]
    public void Evaluator_LowerPriorityEvaluatedFirst()
    {
        var g1 = ErrorGroup("low-num", new ContainsMatcher { Value = "error" }, priority: 1);
        var g2 = ErrorGroup("high-num", new ContainsMatcher { Value = "error" }, priority: 20);
        var policy = PolicyWith(g2, g1); // intentionally reversed in array
        var ev = new RetryPolicyEvaluator(policy);
        var decision = ev.Evaluate(XchangeResultType.Error, "error occurred", 0);
        Assert.AreEqual("low-num", decision.MatchedGroupName);
    }

    [TestMethod]
    public void Evaluator_OnlyMatchingGroupFires()
    {
        var g1 = ErrorGroup("timeouts", new ContainsMatcher { Value = "timeout" }, priority: 1);
        var g2 = ErrorGroup("disk", new ContainsMatcher { Value = "disk" }, priority: 20);
        var policy = PolicyWith(g1, g2);
        var ev = new RetryPolicyEvaluator(policy);
        var decision = ev.Evaluate(XchangeResultType.Error, "disk full", 0);
        Assert.AreEqual("disk", decision.MatchedGroupName);
    }

    // ─── Evaluator: budget — MaxAttemptsPerError ─────────────────────────────────

    [TestMethod]
    public void Evaluator_MaxAttemptsPerError_BlocksAfterCap()
    {
        var policy = PolicyWith(ErrorGroup("transient", new ContainsMatcher { Value = "err" }, maxPerError: 2));
        var ev = new RetryPolicyEvaluator(policy);

        Assert.IsTrue(ev.Evaluate(XchangeResultType.Error, "err", 0).ShouldRetry);
        Assert.IsTrue(ev.Evaluate(XchangeResultType.Error, "err", 1).ShouldRetry);
        Assert.IsFalse(ev.Evaluate(XchangeResultType.Error, "err", 2).ShouldRetry); // cap = 2
    }

    // ─── Evaluator: budget — MaxAttemptsTotal ────────────────────────────────────

    [TestMethod]
    public void Evaluator_MaxAttemptsTotal_BlocksAfterGroupCap()
    {
        var policy = PolicyWith(ErrorGroup("transient", new ContainsMatcher { Value = "err" },
            maxPerError: 100, maxTotal: 3));
        var ev = new RetryPolicyEvaluator(policy);

        // Three different "messages" (attempt index 0 each time) exhaust the group total
        Assert.IsTrue(ev.Evaluate(XchangeResultType.Error, "err", 0).ShouldRetry);
        Assert.IsTrue(ev.Evaluate(XchangeResultType.Error, "err", 0).ShouldRetry);
        Assert.IsTrue(ev.Evaluate(XchangeResultType.Error, "err", 0).ShouldRetry);
        Assert.IsFalse(ev.Evaluate(XchangeResultType.Error, "err", 0).ShouldRetry); // exceeded total=3
    }

    // ─── Evaluator: delay strategies ─────────────────────────────────────────────

    [TestMethod]
    public void FixedDelay_SameEveryAttempt()
    {
        var s = new FixedDelayStrategy { DelayMs = 2000 };
        Assert.AreEqual(TimeSpan.FromMilliseconds(2000), s.GetDelay(0));
        Assert.AreEqual(TimeSpan.FromMilliseconds(2000), s.GetDelay(5));
    }

    [TestMethod]
    public void LinearDelay_GrowsByIncrement()
    {
        var s = new LinearDelayStrategy { InitialDelayMs = 1000, IncrementMs = 500 };
        Assert.AreEqual(TimeSpan.FromMilliseconds(1000), s.GetDelay(0));
        Assert.AreEqual(TimeSpan.FromMilliseconds(1500), s.GetDelay(1));
        Assert.AreEqual(TimeSpan.FromMilliseconds(2000), s.GetDelay(2));
    }

    [TestMethod]
    public void ExponentialDelay_DoublesAndCaps()
    {
        var s = new ExponentialDelayStrategy { InitialDelayMs = 1000, MaxDelayMs = 8000 };
        Assert.AreEqual(TimeSpan.FromMilliseconds(1000), s.GetDelay(0));
        Assert.AreEqual(TimeSpan.FromMilliseconds(2000), s.GetDelay(1));
        Assert.AreEqual(TimeSpan.FromMilliseconds(4000), s.GetDelay(2));
        Assert.AreEqual(TimeSpan.FromMilliseconds(8000), s.GetDelay(3));
        Assert.AreEqual(TimeSpan.FromMilliseconds(8000), s.GetDelay(4)); // capped
    }

    [TestMethod]
    public void Evaluator_DelayFromStrategy_ReturnsCorrectValue()
    {
        var policy = PolicyWith(ErrorGroup("transient",
            new ContainsMatcher { Value = "err" },
            delay: new FixedDelayStrategy { DelayMs = 3000 }));
        var ev = new RetryPolicyEvaluator(policy);
        var decision = ev.Evaluate(XchangeResultType.Error, "err", 0);
        Assert.AreEqual(TimeSpan.FromMilliseconds(3000), decision.Delay);
    }

    // ─── Evaluator: GroupAttemptCounts persistence ───────────────────────────────

    [TestMethod]
    public void GroupAttemptCounts_RestoredAcrossEvaluators_ContinuesBudget()
    {
        var policy = PolicyWith(ErrorGroup("transient", new ContainsMatcher { Value = "err" },
            maxPerError: 100, maxTotal: 2));

        var ev1 = new RetryPolicyEvaluator(policy);
        Assert.IsTrue(ev1.Evaluate(XchangeResultType.Error, "err", 0).ShouldRetry); // group count = 1
        var counts = ev1.GetGroupAttemptCounts();

        // Simulate the next evaluator instance restoring saved state
        var ev2 = new RetryPolicyEvaluator(policy);
        ev2.RestoreGroupAttemptCounts(counts); // group count restored to 1
        Assert.IsTrue(ev2.Evaluate(XchangeResultType.Error, "err", 0).ShouldRetry); // group count = 2
        var counts2 = ev2.GetGroupAttemptCounts();

        var ev3 = new RetryPolicyEvaluator(policy);
        ev3.RestoreGroupAttemptCounts(counts2); // group count restored to 2
        Assert.IsFalse(ev3.Evaluate(XchangeResultType.Error, "err", 0).ShouldRetry); // exceeded total=2
    }

    [TestMethod]
    public void GroupAttemptCounts_WithoutRestore_BudgetResetsToZero()
    {
        var policy = PolicyWith(ErrorGroup("transient", new ContainsMatcher { Value = "err" },
            maxPerError: 100, maxTotal: 1));

        var ev1 = new RetryPolicyEvaluator(policy);
        Assert.IsTrue(ev1.Evaluate(XchangeResultType.Error, "err", 0).ShouldRetry); // exhausted

        // Fresh evaluator without restore — budget starts from 0 again
        var ev2 = new RetryPolicyEvaluator(policy);
        Assert.IsTrue(ev2.Evaluate(XchangeResultType.Error, "err", 0).ShouldRetry);
    }

    [TestMethod]
    public void GetGroupAttemptCounts_ReturnsNonEmptyAfterMatch()
    {
        var policy = PolicyWith(ErrorGroup("transient", new ContainsMatcher { Value = "err" }));
        var ev = new RetryPolicyEvaluator(policy);
        ev.Evaluate(XchangeResultType.Error, "err", 0);
        var counts = ev.GetGroupAttemptCounts();
        Assert.IsTrue(counts.Count > 0);
    }

    // ─── Evaluator: Block action ─────────────────────────────────────────────────

    [TestMethod]
    public void Evaluator_BlockAction_NeverRetries()
    {
        var blockGroup = new RetryGroup
        {
            Name = "permanent-errors",
            Priority = 1,
            Enabled = true,
            AppliesTo = [XchangeResultType.Error],
            Action = RetryAction.Block,
            Matchers = [new ContainsMatcher { Value = "fatal" }],
            Budget = null
        };
        var policy = PolicyWith(blockGroup);
        var ev = new RetryPolicyEvaluator(policy);
        var decision = ev.Evaluate(XchangeResultType.Error, "fatal: cannot recover", 0);
        Assert.IsFalse(decision.ShouldRetry);
    }
}
