using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SW.Bitween.UnitTests;

/// <summary>
/// Reading which global values and partner properties a set of adapter properties names, the way
/// the resolver will fill them in.
/// </summary>
[TestClass]
public class ReferenceTokensTests
{
    [TestMethod]
    public void Every_token_in_a_value_counts_wherever_it_sits()
    {
        var refs = StartupValuesFiller.ReferencesIn([
            "https://{{globals.spaces.host}}/{{globals.spaces.bucket}}?key={{partner.apiKey}}",
            null,
            "{{globals.mail.password}}"
        ]);

        CollectionAssert.AreEquivalent(new[] { "spaces", "mail" }, refs.Globals.Select(g => g.SetId).ToList());
        CollectionAssert.AreEquivalent(new[] { "host", "bucket" }, refs.Globals.Single(g => g.SetId == "spaces").Keys);
        CollectionAssert.AreEqual(new[] { "apiKey" }, refs.PartnerProperties);
    }

    [TestMethod]
    public void Spellings_that_differ_only_in_case_are_one_reference()
    {
        var refs = StartupValuesFiller.ReferencesIn([
            "{{globals.Spaces.secretKey}}", "{{GLOBALS.spaces.SECRETKEY}}", "{{partner.ApiKey}}", "{{Partner.apikey}}"
        ]);

        var set = refs.Globals.Single();
        Assert.AreEqual("Spaces", set.SetId);
        CollectionAssert.AreEqual(new[] { "secretKey" }, set.Keys);
        CollectionAssert.AreEqual(new[] { "ApiKey" }, refs.PartnerProperties);
    }

    /// <summary>Only what the globals and partner fillers would resolve - other tokens, and a globals token with no key, name nothing.</summary>
    [TestMethod]
    public void Other_tokens_and_a_globals_token_without_a_key_are_not_references()
    {
        var refs = StartupValuesFiller.ReferencesIn([
            "{{source.order.id}}", "{{connections.crm.authorization}}", "{{globals.spaces}}", "plain value"
        ]);

        Assert.AreEqual(0, refs.Globals.Count);
        Assert.AreEqual(0, refs.PartnerProperties.Count);
    }
}
