using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.Resources.XchangeExplanations;
using SW.Bitween.Services.Ai;

namespace SW.Bitween.UnitTests.Ai;

/// <summary>
/// The retry chain as an explanation sends it: numbered from the chain's real first attempt, each
/// error masked, and a line masking couldn't be sure of withheld rather than sent.
/// </summary>
[TestClass]
public class ExplainChainTests
{
    static readonly DateTime At = new(2026, 11, 3, 9, 0, 0);

    static Explain.Attempt Failed(string id, int number, string error) => new(id, number, At, false, false, error);

    static HashSet<string> All(Explain.RetryChain chain) => chain.Attempts.Select(a => a.Id).ToHashSet();

    [TestMethod]
    public void A_long_chain_is_numbered_from_its_real_first_attempt_and_says_what_it_left_out()
    {
        var chain = new Explain.RetryChain(
            [Failed("a", 41, "Timeout"), Failed("b", 42, "Timeout"), new("c", 43, At, true, false, null)],
            Position: 42, Total: 43, More: false, EarlierNotListed: 40);

        var lines = Explain.ChainLines(chain, "b", new Masking(), All(chain)).ToList();

        CollectionAssert.AreEqual(new[]
        {
            "(40 earlier attempts not listed)",
            "Attempt 41, from 2026-11-03 09:00 UTC: failed: Timeout",
            "Attempt 42 (this one), from 2026-11-03 09:00 UTC: failed: Timeout",
            "Attempt 43, from 2026-11-03 09:00 UTC: succeeded",
        }, lines);
    }

    [TestMethod]
    public void An_attempts_error_loses_the_values_its_documents_hold()
    {
        var masking = new Masking();
        masking.Document("""{ "customer": "Lina Haddad" }""", "application/json");
        var chain = new Explain.RetryChain([Failed("a", 1, "Customer Lina Haddad was rejected"), Failed("b", 2, "Timeout")], 2, 2, false, 0);

        var line = Explain.ChainLines(chain, "b", masking, All(chain)).First();

        Assert.IsFalse(line.Contains("Lina") || line.Contains("Haddad"), line);
        StringAssert.Contains(line, "was rejected");
    }

    [TestMethod]
    public void An_attempt_whose_documents_couldnt_be_read_has_its_line_withheld()
    {
        var chain = new Explain.RetryChain([Failed("a", 1, "Customer Lina Haddad was rejected"), Failed("b", 2, "Timeout")], 2, 2, false, 0);

        var line = Explain.ChainLines(chain, "b", new Masking(), new HashSet<string> { "b" }).First();

        Assert.AreEqual("Attempt 1, from 2026-11-03 09:00 UTC: failed, with a different error, not sent: its documents couldn't be read to mask it", line);
    }

    [TestMethod]
    public void A_first_line_read_only_in_part_loses_its_last_word()
    {
        var text = "Refused for customer " + new string('y', Explain.ErrorRead);
        Assert.AreEqual("Refused for customer ...", Explain.WholeFirstLine(text[..Explain.ErrorRead]));
        var quoted = "Refused for 'Lina " + new string('y', Explain.ErrorRead);
        Assert.AreEqual("Refused for ...", Explain.WholeFirstLine(quoted[..Explain.ErrorRead]));
        Assert.AreEqual("Timeout", Explain.WholeFirstLine("Timeout\n   at Foo()"));
    }
}
