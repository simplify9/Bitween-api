using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.Model.AutoRetry;
using SW.Bitween.NativeAdapters;

namespace SW.Bitween.UnitTests;

[TestClass]
public class RetryAfterHintTests
{
    [DataTestMethod]
    [DataRow("System.Exception: {{retryAfter:30}} slow down", 30)]
    [DataRow("ValueError: {{retryAfter:5}} x", 5)]
    [DataRow("System.Exception: outer\n ---> Acme.RetryLaterException: {{retryAfter:45}} inner", 45)]
    public void The_wait_is_read_at_the_start_of_an_exception_s_message(string error, int seconds) =>
        Assert.AreEqual(TimeSpan.FromSeconds(seconds), RetryAfterHint.Read(error));

    /// <summary>Quoted later in a message - a partner's body echoed into an error - it asks for nothing.</summary>
    [DataTestMethod]
    [DataRow("System.Exception: 400: BadRequest: {\"note\":\"{{retryAfter:99999}}\"}")]
    [DataRow("{{retryAfter:30}} with no exception before it")]
    [DataRow("System.Exception: {{retryAfter:-5}} negative")]
    [DataRow("System.Exception: {{retryAfter:1234567890}} too many digits")]
    [DataRow("")]
    [DataRow(null)]
    public void Anything_else_asks_for_nothing(string error) =>
        Assert.IsNull(RetryAfterHint.Read(error));

    [TestMethod]
    public void The_built_in_adapters_exception_and_the_reader_agree_and_the_outer_text_is_unchanged()
    {
        var error = new Exception("429: TooManyRequests: slow", new RetryLaterException(TimeSpan.FromSeconds(12.2), "asked to wait")).ToString();
        StringAssert.StartsWith(error, "System.Exception: 429: TooManyRequests: slow");
        Assert.AreEqual(TimeSpan.FromSeconds(13), RetryAfterHint.Read(error));
        Assert.AreEqual("{{retryAfter:13}}", RetryAfterHint.Prefix(TimeSpan.FromSeconds(12.2)));
    }

    [TestMethod]
    public void A_retry_after_date_is_the_time_until_it()
    {
        var now = new DateTimeOffset(2026, 10, 13, 10, 0, 0, TimeSpan.Zero);
        Assert.AreEqual(TimeSpan.FromMinutes(5), RetryLaterException.From(new RetryConditionHeaderValue(now.AddMinutes(5)), now));
        Assert.AreEqual(TimeSpan.Zero, RetryLaterException.From(new RetryConditionHeaderValue(now.AddMinutes(-5)), now));
        Assert.AreEqual(TimeSpan.FromSeconds(7), RetryLaterException.From(new RetryConditionHeaderValue(TimeSpan.FromSeconds(7)), now));
        Assert.IsNull(RetryLaterException.From(null, now));
    }
}
