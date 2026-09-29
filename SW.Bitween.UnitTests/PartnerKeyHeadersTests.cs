using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SW.Bitween.UnitTests;

/// <summary>
/// Which names partners' keys can travel under, whether set system-wide or on one gateway. A bad
/// name saves fine and fails only when a partner calls, so it is refused when it's set instead.
/// </summary>
[TestClass]
public class PartnerKeyHeadersTests
{
    [DataTestMethod]
    [DataRow("partnerkey")]
    [DataRow("X-Api-Key")]
    [DataRow("x_orders_key")]
    [DataRow("apikey")]
    public void A_plain_header_name_can_be_used(string name) =>
        Assert.IsNull(PartnerKeyHeaders.Problem(name));

    [DataTestMethod]
    [DataRow(null)]
    [DataRow("  ")]
    [DataRow("x api key")]      // a space ends a header name
    [DataRow("x-api-key:")]
    [DataRow("Authorization")]  // Bearer and Basic arrive here
    [DataRow("content-type")]
    [DataRow("Wait-Period")]    // the gateway reads it for sync calls
    public void A_name_that_is_not_a_header_or_already_means_something_is_refused(string name) =>
        Assert.IsNotNull(PartnerKeyHeaders.Problem(name));
}
