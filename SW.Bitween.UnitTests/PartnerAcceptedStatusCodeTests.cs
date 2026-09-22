using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.Domain;

namespace SW.Bitween.UnitTests;

/// <summary>
/// The rule behind a partner's accepted response code: its own choice when it has one, and the
/// caller's default when it does not. Kept in one place because the two endpoints that answer
/// have different defaults — the API falls back to the instance-wide setting, the gateway to the
/// 200 it has always sent.
/// </summary>
[TestClass]
public class PartnerAcceptedStatusCodeTests
{
    [DataTestMethod]
    [DataRow(200, true)]
    [DataRow(202, false)]
    public void APartnerWithAChoice_IsAnsweredWithIt(int code, bool expectsOk)
    {
        var partner = new Partner("northwind") { AcceptedResponseStatusCode = code };

        // Whatever the caller would otherwise have used is overridden either way.
        Assert.AreEqual(expectsOk, partner.AnswersOkWhenEmpty(200));
        Assert.AreEqual(expectsOk, partner.AnswersOkWhenEmpty(202));
    }

    [DataTestMethod]
    [DataRow(200, true)]
    [DataRow(202, false)]
    [DataRow(null, false)]
    public void APartnerWithoutOne_LeavesItToTheCaller(int? fallback, bool expectsOk)
    {
        var partner = new Partner("northwind");

        Assert.IsNull(partner.AcceptedResponseStatusCode);
        Assert.AreEqual(expectsOk, partner.AnswersOkWhenEmpty(fallback));
    }

    /// <summary>
    /// 200 and 202 are the only codes the reply can express, so nothing else may be stored —
    /// a partner set to 204 would be answered 202 and nobody would know why.
    /// </summary>
    [DataTestMethod]
    [DataRow(null, true)]
    [DataRow(200, true)]
    [DataRow(202, true)]
    [DataRow(204, false)]
    [DataRow(201, false)]
    [DataRow(500, false)]
    public void OnlyTheTwoCodesTheReplyCanExpress_AreStorable(int? code, bool valid)
    {
        Assert.AreEqual(valid, Partner.IsValidAcceptedResponseStatusCode(code));
    }
}
