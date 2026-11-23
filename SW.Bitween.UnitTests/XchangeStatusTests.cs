using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.Domain;
using SW.PrimitiveTypes;

namespace SW.Bitween.UnitTests;

/// <summary>
/// The status kept on an exchange says what the Exchanges page has always worked out from its result:
/// failed, bad response, or success, which is left blank.
/// </summary>
[TestClass]
public class XchangeStatusTests
{
    static Xchange NewXchange() => new(1, null, new XchangeFile("{}"));

    [TestMethod]
    public void A_new_exchange_is_processing_and_not_retried()
    {
        var xchange = NewXchange();
        Assert.AreEqual(XchangeStatus.Processing, xchange.Status);
        Assert.IsNull(xchange.Retried);
    }

    [TestMethod]
    public void A_success_leaves_the_status_blank()
    {
        var xchange = NewXchange();
        xchange.RecordOutcome(new XchangeResult(xchange.Id, null, null, new XchangeFile("ok", "r.json")));
        Assert.IsNull(xchange.Status);
    }

    [TestMethod]
    public void A_success_with_a_bad_response_is_a_bad_response()
    {
        var xchange = NewXchange();
        xchange.RecordOutcome(new XchangeResult(xchange.Id, null, null, new XchangeFile("no", "r.json", true)));
        Assert.AreEqual(XchangeStatus.BadResponse, xchange.Status);
    }

    [TestMethod]
    public void A_failure_is_failed_even_with_a_bad_response()
    {
        var xchange = NewXchange();
        xchange.RecordOutcome(new XchangeResult(xchange.Id, null, null, new XchangeFile("no", "r.json", true),
            exception: "boom"));
        Assert.AreEqual(XchangeStatus.Failed, xchange.Status);
    }

    [TestMethod]
    public void A_later_outcome_replaces_an_earlier_one()
    {
        // What the failure path relies on when the success path's own save is what failed.
        var xchange = NewXchange();
        xchange.RecordOutcome(new XchangeResult(xchange.Id, null, null));
        xchange.RecordOutcome(new XchangeResult(xchange.Id, null, null, exception: "save failed"));
        Assert.AreEqual(XchangeStatus.Failed, xchange.Status);
    }
}
