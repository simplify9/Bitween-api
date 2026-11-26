using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.Domain;
using SW.PrimitiveTypes;

namespace SW.Bitween.UnitTests;

/// <summary>A retry publishes the bus response its subscription is set up for, as the first attempt did.</summary>
[TestClass]
public class XchangeRetryResponseTests
{
    static Subscription Publishing(string message) =>
        new("Orders", 1) { ResponseMessageTypeName = message };

    [TestMethod]
    public void A_plain_retry_keeps_the_response_of_the_attempt_it_retries()
    {
        var first = new Xchange(Publishing("Order Placed"), new XchangeFile("{}"));
        var retry = new Xchange(first, new XchangeFile("{}"), null, manualRetry: true);
        Assert.AreEqual("Order Placed", retry.ResponseMessageTypeName);
    }

    [TestMethod]
    public void A_retry_with_the_current_settings_takes_the_subscription_s_response_now()
    {
        var first = new Xchange(Publishing("Order Placed"), new XchangeFile("{}"));
        var retry = new Xchange(Publishing("Order Placed v2"), first, new XchangeFile("{}"), manualRetry: true);
        Assert.AreEqual("Order Placed v2", retry.ResponseMessageTypeName);
    }
}
