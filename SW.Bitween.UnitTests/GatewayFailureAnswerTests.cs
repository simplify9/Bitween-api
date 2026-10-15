using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using SW.Bitween.Controllers;
using SW.Bitween.Model;

namespace SW.Bitween.UnitTests;

/// <summary>
/// What a sync gateway call is told when the delivery behind it failed: 400 for gateways that answer as
/// they always did, otherwise what Bitween will do next.
/// </summary>
[TestClass]
public class GatewayFailureAnswerTests
{
    static readonly DateTime Next = new(2026, 10, 12, 8, 20, 0, DateTimeKind.Utc);

    static JObject Body(GatewayFailureAnswers.Answer answer) => JObject.FromObject(answer.Body);

    [TestMethod]
    public void A_legacy_gateway_answers_400_with_nothing_whatever_happened()
    {
        foreach (var retryOn in new DateTime?[] { null, Next })
        {
            var answer = GatewayFailureAnswers.For(GatewayFailureAnswer.Legacy, "x1", hasMapper: true, mapped: true, retryOn);
            Assert.AreEqual(400, answer.Status);
            Assert.IsNull(answer.Body);
        }
    }

    [TestMethod]
    public void A_failure_held_for_the_budget_is_accepted_as_held_with_no_attempt_time()
    {
        var answer = GatewayFailureAnswers.For(GatewayFailureAnswer.Accurate, "x1", hasMapper: true, mapped: true,
            SW.Bitween.Domain.DelayedRetry.HeldOn);
        Assert.AreEqual(202, answer.Status);
        Assert.AreEqual("held", (string)Body(answer)["status"]);
        Assert.IsNull(Body(answer)["nextAttemptOn"]);
    }

    [TestMethod]
    public void A_failure_Bitween_will_retry_is_accepted_so_the_caller_does_not_send_it_again()
    {
        var answer = GatewayFailureAnswers.For(GatewayFailureAnswer.Accurate, "x1", hasMapper: true, mapped: true, Next);
        Assert.AreEqual(202, answer.Status);
        Assert.AreEqual("x1", (string)Body(answer)["exchangeId"]);
        Assert.AreEqual("retrying", (string)Body(answer)["status"]);
        Assert.AreEqual(Next, (DateTime)Body(answer)["nextAttemptOn"]);
    }

    [TestMethod]
    public void A_delivery_that_failed_for_good_is_a_bad_gateway_and_a_message_that_could_not_be_mapped_is_unprocessable()
    {
        var delivery = GatewayFailureAnswers.For(GatewayFailureAnswer.Accurate, "x2", hasMapper: true, mapped: true, null);
        Assert.AreEqual(502, delivery.Status);
        Assert.AreEqual("failed", (string)Body(delivery)["status"]);

        // No mapper: what failed was the delivery.
        Assert.AreEqual(502, GatewayFailureAnswers.For(GatewayFailureAnswer.Accurate, "x3", hasMapper: false, mapped: false, null).Status);

        var mapping = GatewayFailureAnswers.For(GatewayFailureAnswer.Accurate, "x4", hasMapper: true, mapped: false, null);
        Assert.AreEqual(422, mapping.Status);
        StringAssert.Contains((string)Body(mapping)["error"], "couldn't be mapped");
    }
}
